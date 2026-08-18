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
        if (command is "--tray" or "--tray-open")
        {
            return RunTray(forceOpen: command == "--tray-open");
        }

        var config = LauncherConfig.Load();
        if (args.Length == 0 || (command == "start" && args.Length == 1))
        {
            return BootstrapTray(config);
        }

        if (command == "doctor")
        {
            return RunDoctor(config, args.Skip(1).ToArray());
        }

        if (args.Length == 1 && command is ("stop" or "restart" or "status" or "open" or "logs" or "exit"))
        {
            return RunControlCommand(config, command);
        }

        if (command == "--foreground")
        {
            return RunForeground(config, args.Skip(1).ToArray());
        }

        return RunForeground(config, args);
    }

    private static int RunTray(bool forceOpen)
    {
        var config = LauncherConfig.Load();
        using var mutex = new Mutex(initiallyOwned: true, config.MutexName, out var createdNew);
        if (!createdNew)
        {
            return 0;
        }

        ApplicationConfiguration.Initialize();
        using var logger = LauncherLogger.Create(config.LogDirectory);
        using var context = new TrayApplicationContext(config, logger, forceOpen || config.AutoOpen);
        Application.Run(context);
        return 0;
    }

    private static int BootstrapTray(LauncherConfig config, bool forceOpen = false)
    {
        if (IsTrayRunning(config.MutexName))
        {
            var existingResponse = ControlClient.TrySendAsync(
                config.PipeName,
                forceOpen ? "activate-open" : "activate",
                TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
            if (existingResponse is not null || IsTrayRunning(config.MutexName))
            {
                return 0;
            }
        }

        var self = GetSelfInvocation(forceOpen ? "--tray-open" : "--tray");
        var process = Process.Start(self);
        if (process is null)
        {
            throw new InvalidOperationException("Could not start the background tray instance.");
        }

        return 0;
    }

    private static bool IsTrayRunning(string mutexName)
    {
        try
        {
            if (!Mutex.TryOpenExisting(mutexName, out var mutex))
            {
                return false;
            }

            mutex.Dispose();
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static int RunControlCommand(LauncherConfig config, string command)
    {
        var timeout = IsTrayRunning(config.MutexName)
            ? TimeSpan.FromSeconds(2)
            : TimeSpan.FromMilliseconds(250);
        var response = ControlClient.TrySendAsync(
            config.PipeName,
            command,
            timeout).GetAwaiter().GetResult();

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
            return BootstrapTray(config, forceOpen: true);
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

    private static int RunDoctor(LauncherConfig config, string[] arguments)
    {
        using var logger = LauncherLogger.Create(config.LogDirectory);
        var json = false;
        var copy = false;
        string? reportPath = null;
        for (var index = 0; index < arguments.Length; index++)
        {
            switch (arguments[index])
            {
                case "--json":
                    json = true;
                    break;
                case "--copy":
                    copy = true;
                    break;
                case "--report" when index + 1 < arguments.Length:
                    reportPath = arguments[++index];
                    break;
                default:
                    throw new InvalidOperationException(
                        "doctor accepts only --json, --copy, and --report <path>.");
            }
        }

        var report = new DoctorRunner(config, logger).RunAsync().GetAwaiter().GetResult();
        var text = json ? report.ToJson() : report.ToText();
        if (reportPath is not null)
        {
            var fullPath = Path.GetFullPath(reportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, fullPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                ? report.ToJson()
                : report.ToText());
            if (!json)
            {
                text += $"{Environment.NewLine}{Environment.NewLine}Saved report: {fullPath}";
            }
        }

        if (copy)
        {
            Clipboard.SetText(report.ToText());
            if (!json)
            {
                text += $"{Environment.NewLine}{Environment.NewLine}Copied the redacted report to the clipboard.";
            }
        }

        if (json)
        {
            NativeMethods.AttachToParentConsole();
            Console.WriteLine(text);
        }
        else
        {
            ShowInformation(text);
        }

        return report.HasFailures ? 1 : 0;
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

    private static ProcessStartInfo GetSelfInvocation(string trayArgument)
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

        startInfo.ArgumentList.Add(trayArgument);
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
