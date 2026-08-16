using System.Diagnostics;
using System.Windows.Forms;

namespace DshLauncher;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            return Run(args);
        }
        catch (Exception exception)
        {
            ShowFatalError(exception.Message);
            return 1;
        }
    }

    private static int Run(string[] args)
    {
        var command = args.FirstOrDefault()?.Trim().ToLowerInvariant();
        if (command == "--tray")
        {
            return RunTray();
        }

        var config = LauncherConfig.Load();
        if (args.Length == 0 || (command == "start" && args.Length == 1))
        {
            return BootstrapTray(config);
        }

        if (args.Length == 1 && command == "doctor")
        {
            return RunDoctor(config);
        }

        if (args.Length == 1 && command is ("stop" or "restart" or "status" or "open" or "logs"))
        {
            return RunControlCommand(config, command);
        }

        if (command == "--foreground")
        {
            return RunForeground(config, args.Skip(1).ToArray());
        }

        return RunForeground(config, args);
    }

    private static int RunTray()
    {
        var config = LauncherConfig.Load();
        using var mutex = new Mutex(initiallyOwned: true, config.MutexName, out var createdNew);
        if (!createdNew)
        {
            return 0;
        }

        ApplicationConfiguration.Initialize();
        using var logger = LauncherLogger.Create(config.LogDirectory);
        using var context = new TrayApplicationContext(config, logger);
        Application.Run(context);
        return 0;
    }

    private static int BootstrapTray(LauncherConfig config)
    {
        var existingResponse = ControlClient.TrySendAsync(
            config.PipeName,
            "start",
            TimeSpan.FromMilliseconds(250)).GetAwaiter().GetResult();
        if (existingResponse is not null)
        {
            return 0;
        }

        var self = GetSelfInvocation();
        var process = Process.Start(self);
        if (process is null)
        {
            throw new InvalidOperationException("Could not start the background tray instance.");
        }

        return 0;
    }

    private static int RunControlCommand(LauncherConfig config, string command)
    {
        var response = ControlClient.TrySendAsync(
            config.PipeName,
            command,
            TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();

        if (response is not null)
        {
            if (command is "status" or "open" or "logs")
            {
                ShowInformation(response);
            }

            return 0;
        }

        if (command == "restart")
        {
            return BootstrapTray(config);
        }

        if (command == "open")
        {
            BrowserLauncher.Open(config.WebUrl);
            return 0;
        }

        if (command == "logs")
        {
            BrowserLauncher.OpenDirectory(config.LogDirectory);
            return 0;
        }

        if (command == "status")
        {
            ShowInformation($"dsh-launcher\n\nNot running.\nWeb: {config.WebUrl}");
        }

        return 0;
    }

    private static int RunForeground(LauncherConfig config, string[] requestedArguments)
    {
        NativeMethods.AttachToParentConsole();
        using var logger = LauncherLogger.Create(config.LogDirectory);
        var resolver = new RunnerResolver(logger);
        var runner = resolver.ResolveAsync().GetAwaiter().GetResult();
        var effectiveArguments = requestedArguments.Length == 0 ? new[] { "web" } : requestedArguments;
        var foregroundRunner = runner with
        {
            PrefixArguments = runner.PrefixArguments.Concat(effectiveArguments).ToArray()
        };
        using var process = ProcessLauncher.Start(
            foregroundRunner,
            redirectOutput: true,
            hiddenWindow: false);
        var outputTask = PumpForegroundOutputAsync(process.StandardOutput, "STDOUT", logger, Console.Out);
        var errorTask = PumpForegroundOutputAsync(process.StandardError, "STDERR", logger, Console.Error);
        process.WaitForExit();
        Task.WaitAll(outputTask, errorTask);

        return process.ExitCode;
    }

    private static int RunDoctor(LauncherConfig config)
    {
        using var logger = LauncherLogger.Create(config.LogDirectory);
        var lines = new List<string>
        {
            "dsh-launcher diagnostics",
            $"Web URL: {config.WebUrl}",
            $"Log directory: {config.LogDirectory}",
        };
        var failed = false;

        try
        {
            var runner = new RunnerResolver(logger).ResolveAsync().GetAwaiter().GetResult();
            lines.Add($"Harness CLI: OK ({runner.Description})");
        }
        catch (Exception exception)
        {
            failed = true;
            lines.Add($"Harness CLI: FAILED ({exception.Message})");
        }

        try
        {
            var probe = WebHealthChecker.ProbeAsync(config.WebUrl, TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
            lines.Add(probe.Responding
                ? $"Web endpoint: RESPONDING (HTTP {probe.StatusCode})"
                : "Web endpoint: NOT RESPONDING (this is normal before dsh starts)");
        }
        catch (Exception exception)
        {
            failed = true;
            lines.Add($"Web endpoint: FAILED ({exception.Message})");
        }

        try
        {
            Directory.CreateDirectory(config.LogDirectory);
            lines.Add("Log directory: writable");
        }
        catch (Exception exception)
        {
            failed = true;
            lines.Add($"Log directory: FAILED ({exception.Message})");
        }

        ShowInformation(string.Join(Environment.NewLine, lines));
        return failed ? 1 : 0;
    }

    private static async Task PumpForegroundOutputAsync(
        StreamReader reader,
        string streamName,
        LauncherLogger logger,
        TextWriter console)
    {
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            console.WriteLine(line);
            console.Flush();
            logger.WriteProcessOutput(streamName, line);
        }
    }

    private static ProcessStartInfo GetSelfInvocation()
    {
        var processPath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrWhiteSpace(processPath))
        {
            throw new InvalidOperationException("Could not locate dsh-launcher executable.");
        }

        var startInfo = new ProcessStartInfo
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = AppContext.BaseDirectory
        };

        var dotnetHost = Path.GetFileNameWithoutExtension(processPath);
        var applicationDll = Path.Combine(AppContext.BaseDirectory, "dsh-launcher.dll");
        if (dotnetHost.Equals("dotnet", StringComparison.OrdinalIgnoreCase) && File.Exists(applicationDll))
        {
            startInfo.FileName = processPath;
            startInfo.ArgumentList.Add(applicationDll);
        }
        else
        {
            startInfo.FileName = processPath;
        }

        startInfo.ArgumentList.Add("--tray");
        return startInfo;
    }

    private static void ShowInformation(string message)
    {
        MessageBox.Show(message, "dsh-launcher", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private static void ShowFatalError(string message)
    {
        try
        {
            MessageBox.Show(
                $"dsh-launcher\n\n{message}",
                "dsh-launcher",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        catch
        {
            Console.Error.WriteLine($"dsh-launcher: {message}");
        }
    }
}
