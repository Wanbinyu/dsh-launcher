using System.Diagnostics;

namespace DshLauncher;

internal sealed record StartResult(
    bool Ready,
    bool Exited,
    int? ExitCode,
    string Message);

internal sealed record ProcessStatus(
    bool Running,
    int? ProcessId,
    int? ExitCode,
    string? RunnerDescription,
    string Message);

internal sealed class ProcessSupervisor : IDisposable
{
    private readonly LauncherConfig _config;
    private readonly LauncherLogger _logger;
    private readonly RunnerResolver _resolver;
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private readonly object _sync = new();
    private Process? _process;
    private RunnerSpec? _runner;
    private int? _lastExitCode;
    private bool _disposed;

    public ProcessSupervisor(LauncherConfig config, LauncherLogger logger)
    {
        _config = config;
        _logger = logger;
        _resolver = new RunnerResolver(logger);
    }

    public event EventHandler? ProcessExited;

    public async Task<StartResult> StartAsync(CancellationToken cancellationToken = default)
    {
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(true);
        try
        {
            ThrowIfDisposed();
            if (GetRunningProcess() is not null)
            {
                return new StartResult(true, false, null, "DeepSeek Harness is already running.");
            }

            _lastExitCode = null;
            var runner = await _resolver.ResolveAsync(cancellationToken).ConfigureAwait(true);
            _logger.Info($"Starting {runner.Description}.");

            var process = ProcessLauncher.Start(runner, redirectOutput: true, hiddenWindow: true);
            process.EnableRaisingEvents = true;
            process.Exited += HandleProcessExited;
            lock (_sync)
            {
                _runner = runner;
                _process = process;
            }

            _ = PumpOutputAsync(process.StandardOutput, "STDOUT");
            _ = PumpOutputAsync(process.StandardError, "STDERR");

            var deadline = DateTimeOffset.UtcNow + _config.StartupTimeout;
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

                if (await WebHealthChecker.IsReadyAsync(_config.WebUrl, TimeSpan.FromMilliseconds(500), cancellationToken)
                    .ConfigureAwait(true))
                {
                    _logger.Info($"Web server is ready at {_config.WebUrl}.");
                    return new StartResult(true, false, null, $"DeepSeek Harness is ready at {_config.WebUrl}.");
                }

                await Task.Delay(250, cancellationToken).ConfigureAwait(true);
            }

            _logger.Info($"Harness is still starting; {_config.WebUrl} did not respond within {_config.StartupTimeout.TotalSeconds:0} seconds.");
            return new StartResult(
                Ready: false,
                Exited: false,
                ExitCode: null,
                Message: $"DeepSeek Harness is still starting. Open {_config.WebUrl} when it is ready.");
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
                _logger.WriteProcessOutput(streamName, line);
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
}
