using System.Diagnostics;
using System.Text.RegularExpressions;

namespace DshLauncher;

internal sealed record StartResult(
    bool Ready,
    bool Exited,
    int? ExitCode,
    string Message,
    Uri? LaunchUrl = null);

internal sealed record ProcessStatus(
    bool Running,
    int? ProcessId,
    int? ExitCode,
    string? RunnerDescription,
    string Message);

internal sealed class ProcessSupervisor : IDisposable
{
    private static readonly Regex DshWebLaunchUrlPattern = new(
        @"^\s*dsh web:\s+(?<url>https?://[^\s)]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex LaunchTokenPattern = new(
        @"([?&]token=)[^&\s)]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly LauncherConfig _config;
    private readonly LauncherLogger _logger;
    private readonly Func<CancellationToken, Task<RunnerSpec>> _resolveRunner;
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private readonly object _sync = new();
    private Process? _process;
    private RunnerSpec? _runner;
    private Uri? _launchUrl;
    private int? _lastExitCode;
    private bool _disposed;

    public ProcessSupervisor(
        LauncherConfig config,
        LauncherLogger logger,
        Func<CancellationToken, Task<RunnerSpec>>? resolveRunner = null)
    {
        _config = config;
        _logger = logger;
        _resolveRunner = resolveRunner ?? new RunnerResolver(logger).ResolveAsync;
    }

    public event EventHandler? ProcessExited;

    public async Task<StartResult> StartAsync(CancellationToken cancellationToken = default)
    {
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(true);
        try
        {
            ThrowIfDisposed();
            var runningProcess = GetRunningProcess();
            if (runningProcess is not null)
            {
                _logger.Info($"Harness process {runningProcess.Id} is already running; waiting for {_config.WebUrl}.");
                return await WaitForWebAsync(runningProcess, cancellationToken).ConfigureAwait(true);
            }

            var existingWeb = await WebHealthChecker.ProbeAsync(
                _config.WebUrl,
                TimeSpan.FromMilliseconds(500),
                cancellationToken).ConfigureAwait(true);
            if (existingWeb.Responding)
            {
                var status = existingWeb.StatusCode.HasValue ? $" (HTTP {existingWeb.StatusCode.Value})" : string.Empty;
                _logger.Info($"Web URL already responds{status}; leaving the existing service unmanaged.");
                return new StartResult(
                    Ready: true,
                    Exited: false,
                    ExitCode: null,
                    Message: $"The configured web URL is already responding{status}. Reusing the existing service.");
            }

            if (existingWeb.RequiresAuthentication)
            {
                _logger.Info("Web URL already responds with HTTP 401 and requires a Harness launch token; leaving it unmanaged.");
                return new StartResult(
                    Ready: false,
                    Exited: false,
                    ExitCode: null,
                    Message:
                        $"The configured web URL responded with HTTP 401 at {_config.WebUrl}, but the launcher has no authenticated dsh web URL for that existing service. " +
                        "Close the existing Harness process or reopen the URL printed by dsh web.");
            }

            _lastExitCode = null;
            SetLaunchUrl(null);
            var runner = await _resolveRunner(cancellationToken).ConfigureAwait(true);
            _logger.Info($"Starting {runner.Description}.");

            var webRunner = AddWebCommand(runner);

            var process = ProcessLauncher.Start(webRunner, redirectOutput: true, hiddenWindow: true);
            process.EnableRaisingEvents = true;
            process.Exited += HandleProcessExited;
            lock (_sync)
            {
                _runner = webRunner;
                _process = process;
            }

            _ = PumpOutputAsync(process.StandardOutput, "STDOUT");
            _ = PumpOutputAsync(process.StandardError, "STDERR");

            return await WaitForWebAsync(process, cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(true);
        try
        {
            Process? process;
            lock (_sync)
            {
                process = _process;
            }

            if (process is null || HasExited(process, out _))
            {
                return;
            }

            _logger.Info($"Stopping Harness process {process.Id}.");
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                return;
            }

            try
            {
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(true);
            }
            catch (InvalidOperationException)
            {
                // The process may have exited between the status check and Kill.
            }
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public ProcessStatus GetStatus()
    {
        Process? process;
        RunnerSpec? runner;
        int? lastExitCode;
        lock (_sync)
        {
            process = _process;
            runner = _runner;
            lastExitCode = _lastExitCode;
        }

        int? exitCode = null;
        if (process is not null && !HasExited(process, out exitCode))
        {
            return new ProcessStatus(
                Running: true,
                ProcessId: process.Id,
                ExitCode: null,
                RunnerDescription: runner?.Description,
                Message: $"Running (PID {process.Id}), web: {_config.WebUrl}");
        }

        return new ProcessStatus(
            Running: false,
            ProcessId: null,
            ExitCode: exitCode ?? lastExitCode,
            RunnerDescription: runner?.Description,
            Message: exitCode.HasValue
                ? $"Stopped (exit code {exitCode.Value}), web: {_config.WebUrl}"
                : $"Stopped, web: {_config.WebUrl}");
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        try
        {
            StopAsync().GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            _logger.Error("Could not stop Harness during shutdown", exception);
        }

        _operationLock.Dispose();
    }

    private Process? GetRunningProcess()
    {
        lock (_sync)
        {
            return _process is not null && !HasExited(_process, out _) ? _process : null;
        }
    }

    private async Task PumpOutputAsync(StreamReader reader, string streamName)
    {
        try
        {
            while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                CaptureLaunchUrl(line, streamName);
                _logger.WriteProcessOutput(streamName, RedactLaunchTokens(line));
            }
        }
        catch (ObjectDisposedException)
        {
        }
        catch (IOException exception)
        {
            _logger.Error($"Could not read Harness {streamName}", exception);
        }
    }

    private void CaptureLaunchUrl(string line, string streamName)
    {
        var launchUrl = TryParseDshWebLaunchUrl(line);
        if (launchUrl is null)
        {
            return;
        }

        if (!IsConfiguredWebEndpoint(launchUrl))
        {
            _logger.Info($"Ignored Harness launch URL from {streamName} because it does not match the configured web endpoint.");
            return;
        }

        SetLaunchUrl(launchUrl);
        _logger.Info($"Captured authenticated Harness launch URL from {streamName}.");
    }

    private void HandleProcessExited(object? sender, EventArgs args)
    {
        if (sender is not Process process)
        {
            return;
        }

        int exitCode;
        try
        {
            exitCode = process.ExitCode;
        }
        catch (InvalidOperationException)
        {
            exitCode = -1;
        }

        lock (_sync)
        {
            if (ReferenceEquals(_process, process))
            {
                _lastExitCode = exitCode;
                _process = null;
            }
        }

        _logger.Info($"Harness process exited with code {exitCode}.");
        ProcessExited?.Invoke(this, EventArgs.Empty);
    }

    private static bool HasExited(Process process, out int? exitCode)
    {
        try
        {
            if (!process.HasExited)
            {
                exitCode = null;
                return false;
            }

            exitCode = process.ExitCode;
            return true;
        }
        catch (InvalidOperationException)
        {
            exitCode = null;
            return true;
        }
    }

    private void ThrowIfDisposed()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(ProcessSupervisor));
            }
        }
    }

    private async Task<StartResult> WaitForWebAsync(Process process, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + _config.StartupTimeout;
        var sawAuthenticationRequired = false;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (HasExited(process, out var exitCode))
            {
                return new StartResult(
                    Ready: false,
                    Exited: true,
                    ExitCode: exitCode,
                    Message: $"DeepSeek Harness exited before the web server became ready (exit code {exitCode}).");
            }

            var launchUrl = GetLaunchUrl();
            var webProbe = await WebHealthChecker.ProbeAsync(
                _config.WebUrl,
                TimeSpan.FromMilliseconds(500),
                cancellationToken).ConfigureAwait(true);
            if (webProbe.Responding)
            {
                var status = webProbe.StatusCode.HasValue ? $" (HTTP {webProbe.StatusCode.Value})" : string.Empty;
                _logger.Info($"Web server is ready at {_config.WebUrl}{status}.");
                return new StartResult(
                    Ready: true,
                    Exited: false,
                    ExitCode: null,
                    Message: $"DeepSeek Harness is ready at {_config.WebUrl}.",
                    LaunchUrl: launchUrl);
            }

            if (webProbe.RequiresAuthentication)
            {
                sawAuthenticationRequired = true;
                if (launchUrl is not null)
                {
                    _logger.Info($"Web server is ready at {_config.WebUrl} (HTTP 401, authentication required).");
                    return new StartResult(
                        Ready: true,
                        Exited: false,
                        ExitCode: null,
                        Message: $"DeepSeek Harness is ready at {_config.WebUrl}; browser authentication is required.",
                        LaunchUrl: launchUrl);
                }
            }

            await Task.Delay(250, cancellationToken).ConfigureAwait(true);
        }

        if (sawAuthenticationRequired)
        {
            _logger.Info($"Harness requires authentication at {_config.WebUrl}, but no authenticated launch URL was captured within {_config.StartupTimeout.TotalSeconds:0} seconds.");
            return new StartResult(
                Ready: false,
                Exited: false,
                ExitCode: null,
                Message:
                    $"DeepSeek Harness responded with HTTP 401 at {_config.WebUrl}, but the launcher did not capture the authenticated dsh web URL within {_config.StartupTimeout.TotalSeconds:0} seconds. " +
                    "Open the logs and look for startup output from dsh web.");
        }

        _logger.Info($"Harness is still starting; {_config.WebUrl} did not respond within {_config.StartupTimeout.TotalSeconds:0} seconds.");
        return new StartResult(
            Ready: false,
            Exited: false,
            ExitCode: null,
            Message:
                $"DeepSeek Harness did not respond at {_config.WebUrl} within {_config.StartupTimeout.TotalSeconds:0} seconds. " +
                "It may still be starting in the background.");
    }

    private Uri? GetLaunchUrl()
    {
        lock (_sync)
        {
            return _launchUrl;
        }
    }

    private void SetLaunchUrl(Uri? launchUrl)
    {
        lock (_sync)
        {
            _launchUrl = launchUrl;
        }
    }

    private bool IsConfiguredWebEndpoint(Uri launchUrl)
    {
        if (!string.Equals(launchUrl.Scheme, _config.WebUrl.Scheme, StringComparison.OrdinalIgnoreCase) ||
            launchUrl.Port != _config.WebUrl.Port)
        {
            return false;
        }

        return string.Equals(launchUrl.Host, _config.WebUrl.Host, StringComparison.OrdinalIgnoreCase) ||
               (launchUrl.IsLoopback && _config.WebUrl.IsLoopback);
    }

    internal static Uri? TryParseDshWebLaunchUrl(string line)
    {
        var match = DshWebLaunchUrlPattern.Match(line);
        if (!match.Success)
        {
            return null;
        }

        var candidate = match.Groups["url"].Value;
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var launchUrl) ||
            (launchUrl.Scheme != Uri.UriSchemeHttp && launchUrl.Scheme != Uri.UriSchemeHttps) ||
            !HasTokenQuery(launchUrl))
        {
            return null;
        }

        return launchUrl;
    }

    internal static string RedactLaunchTokens(string line) =>
        LaunchTokenPattern.Replace(line, "$1<redacted>");

    private static bool HasTokenQuery(Uri launchUrl)
    {
        var query = launchUrl.Query;
        if (query.Length <= 1)
        {
            return false;
        }

        foreach (var part in query[1..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = part.IndexOf('=');
            var name = separator >= 0 ? part[..separator] : part;
            if (string.Equals(Uri.UnescapeDataString(name), "token", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    internal static RunnerSpec AddWebCommand(RunnerSpec runner)
    {
        var arguments = runner.PrefixArguments.Append("web");
        if (SupportsNoOpen(runner.DshVersion))
        {
            arguments = arguments.Append("--no-open");
        }

        return runner with
        {
            PrefixArguments = arguments.ToArray()
        };
    }

    internal static bool SupportsNoOpen(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return false;
        }

        var normalized = version.Trim().TrimStart('v');
        var metadataIndex = normalized.IndexOf('+');
        if (metadataIndex >= 0)
        {
            normalized = normalized[..metadataIndex];
        }

        var prereleaseIndex = normalized.IndexOf('-');
        var core = prereleaseIndex >= 0 ? normalized[..prereleaseIndex] : normalized;
        if (!Version.TryParse(core, out var parsed))
        {
            return false;
        }

        var baseline = new Version(0, 1, 0);
        var comparison = parsed.CompareTo(baseline);
        if (comparison != 0)
        {
            return comparison > 0;
        }

        if (prereleaseIndex < 0)
        {
            return true;
        }

        const string rcPrefix = "rc.";
        var prerelease = normalized[(prereleaseIndex + 1)..];
        return prerelease.StartsWith(rcPrefix, StringComparison.OrdinalIgnoreCase) &&
               int.TryParse(prerelease[rcPrefix.Length..], out var candidate) &&
               candidate >= 8;
    }
}
