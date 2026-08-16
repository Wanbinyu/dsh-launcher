using System.IO.Pipes;
using System.Threading;
using System.Windows.Forms;

namespace DshLauncher;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly LauncherConfig _config;
    private readonly LauncherLogger _logger;
    private readonly ProcessSupervisor _supervisor;
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu;
    private readonly SynchronizationContext _uiContext;
    private readonly CancellationTokenSource _controlCancellation = new();
    private bool _exiting;
    private string _status = "starting";

    public TrayApplicationContext(LauncherConfig config, LauncherLogger logger)
    {
        _config = config;
        _logger = logger;
        _uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _supervisor = new ProcessSupervisor(config, logger);
        _supervisor.ProcessExited += HandleProcessExited;

        _menu = CreateMenu();
        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "dsh-launcher: starting",
            Visible = true,
            ContextMenuStrip = _menu
        };
        _notifyIcon.DoubleClick += HandleOpenClick;

        _logger.Info($"Tray instance started. Web URL: {_config.WebUrl}.");
        _ = RunControlServerAsync();
        _ = StartAndOpenAsync(openBrowser: true, showErrors: true);
    }

    private ContextMenuStrip CreateMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(CreateMenuItem("启动 / Start", async (_, _) => await StartAndOpenAsync(true, true)));
        menu.Items.Add(CreateMenuItem("打开网页 / Open web", HandleOpenClick));
        menu.Items.Add(CreateMenuItem("查看状态 / Status", HandleStatusClick));
        menu.Items.Add(CreateMenuItem("重启 / Restart", async (_, _) => await RestartAsync()));
        menu.Items.Add(CreateMenuItem("停止 / Stop", async (_, _) => await StopAsync()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(CreateMenuItem("打开日志目录 / Open logs", HandleLogsClick));
        menu.Items.Add(CreateMenuItem("退出 / Exit", (_, _) => ExitApplication()));
        return menu;
    }

    private static ToolStripMenuItem CreateMenuItem(string text, EventHandler handler)
    {
        var item = new ToolStripMenuItem(text);
        item.Click += handler;
        return item;
    }

    private async void HandleOpenClick(object? sender, EventArgs args)
    {
        try
        {
            BrowserLauncher.Open(_config.WebUrl);
        }
        catch (Exception exception)
        {
            _logger.Error("Could not open the web browser", exception);
            ShowError($"无法打开浏览器 / Could not open the browser:\n{exception.Message}");
        }

        await Task.CompletedTask;
    }

    private void HandleStatusClick(object? sender, EventArgs args)
    {
        ShowStatus(BuildStatusMessage());
    }

    private void HandleLogsClick(object? sender, EventArgs args)
    {
        try
        {
            BrowserLauncher.OpenDirectory(_config.LogDirectory);
        }
        catch (Exception exception)
        {
            _logger.Error("Could not open the log directory", exception);
            ShowError($"无法打开日志目录 / Could not open logs:\n{exception.Message}");
        }
    }

    private async Task<string> StartAndOpenAsync(bool openBrowser, bool showErrors)
    {
        SetStatus("starting");
        try
        {
            var result = await _supervisor.StartAsync();
            if (result.Ready && openBrowser && _config.AutoOpen)
            {
                BrowserLauncher.Open(_config.WebUrl);
            }

            if (result.Exited)
            {
                SetStatus("stopped");
                if (showErrors)
                {
                    ShowError(result.Message);
                }
            }
            else if (result.Ready)
            {
                SetStatus("running");
            }
            else
            {
                SetStatus("starting");
            }

            return result.Message;
        }
        catch (OperationCanceledException)
        {
            SetStatus("stopped");
            return "Start cancelled.";
        }
        catch (Exception exception)
        {
            _logger.Error("Could not start DeepSeek Harness", exception);
            SetStatus("error");
            if (showErrors)
            {
                ShowError($"启动 DeepSeek Harness 失败 / Could not start DeepSeek Harness:\n{exception.Message}");
            }

            return $"Could not start DeepSeek Harness: {exception.Message}";
        }
    }

    private async Task RestartAsync()
    {
        SetStatus("restarting");
        try
        {
            await _supervisor.StopAsync();
            await StartAndOpenAsync(openBrowser: true, showErrors: true);
        }
        catch (Exception exception)
        {
            _logger.Error("Could not restart DeepSeek Harness", exception);
            SetStatus("error");
            ShowError($"重启 DeepSeek Harness 失败 / Could not restart:\n{exception.Message}");
        }
    }

    private async Task StopAsync()
    {
        try
        {
            await _supervisor.StopAsync();
            SetStatus("stopped");
        }
        catch (Exception exception)
        {
            _logger.Error("Could not stop DeepSeek Harness", exception);
            ShowError($"停止 DeepSeek Harness 失败 / Could not stop:\n{exception.Message}");
        }
    }

    private string BuildStatusMessage()
    {
        var processStatus = _supervisor.GetStatus();
        var runner = string.IsNullOrWhiteSpace(processStatus.RunnerDescription)
            ? string.Empty
            : $"\nRunner: {processStatus.RunnerDescription}";
        return $"dsh-launcher\n\n{processStatus.Message}\nTray: {_status}{runner}";
    }

    private async Task RunControlServerAsync()
    {
        while (!_controlCancellation.IsCancellationRequested)
        {
            using var server = new NamedPipeServerStream(
                _config.PipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);
            try
            {
                await server.WaitForConnectionAsync(_controlCancellation.Token).ConfigureAwait(false);
                using var reader = new StreamReader(server);
                using var writer = new StreamWriter(server) { AutoFlush = true };
                var command = await reader.ReadLineAsync(_controlCancellation.Token).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(command))
                {
                    var response = await RunOnUiAsync(() => HandleControlCommandAsync(command.Trim()))
                        .ConfigureAwait(false);
                    await writer.WriteLineAsync(response).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (IOException exception) when (!_controlCancellation.IsCancellationRequested)
            {
                _logger.Error("Control pipe error", exception);
            }
            catch (ObjectDisposedException)
            {
                break;
            }
        }
    }

    private async Task<string> HandleControlCommandAsync(string command)
    {
        return command.ToLowerInvariant() switch
        {
            "start" => await StartAndOpenAsync(openBrowser: true, showErrors: false),
            "restart" => await RestartFromControlAsync(),
            "stop" => await StopFromControlAsync(),
            "status" => BuildStatusMessage().Replace(Environment.NewLine, " | "),
            "open" => OpenFromControl(),
            "logs" => OpenLogsFromControl(),
            _ => $"Unknown launcher command: {command}"
        };
    }

    private async Task<string> RestartFromControlAsync()
    {
        await RestartAsync();
        return BuildStatusMessage().Replace(Environment.NewLine, " | ");
    }

    private async Task<string> StopFromControlAsync()
    {
        await StopAsync();
        return "DeepSeek Harness stopped.";
    }

    private string OpenFromControl()
    {
        try
        {
            BrowserLauncher.Open(_config.WebUrl);
            return $"Opened {_config.WebUrl}";
        }
        catch (Exception exception)
        {
            _logger.Error("Could not open the web browser", exception);
            return $"Could not open the browser: {exception.Message}";
        }
    }

    private string OpenLogsFromControl()
    {
        try
        {
            BrowserLauncher.OpenDirectory(_config.LogDirectory);
            return $"Opened {_config.LogDirectory}";
        }
        catch (Exception exception)
        {
            _logger.Error("Could not open the log directory", exception);
            return $"Could not open logs: {exception.Message}";
        }
    }

    private Task<T> RunOnUiAsync<T>(Func<Task<T>> action)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (_exiting)
        {
            completion.SetException(new ObjectDisposedException(nameof(TrayApplicationContext)));
            return completion.Task;
        }

        _uiContext.Post(async _ =>
        {
            try
            {
                completion.SetResult(await action());
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        }, null);
        return completion.Task;
    }

    private void HandleProcessExited(object? sender, EventArgs args)
    {
        if (_exiting)
        {
            return;
        }

        _uiContext.Post(_ => SetStatus("stopped"), null);
    }

    private void SetStatus(string status)
    {
        _status = status;
        if (_notifyIcon is null)
        {
            return;
        }

        var text = $"dsh-launcher: {status}";
        _notifyIcon.Text = text.Length <= 63 ? text : text[..63];
    }

    private void ShowStatus(string message)
    {
        MessageBox.Show(message, "dsh-launcher", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ShowError(string message)
    {
        MessageBox.Show(message, "dsh-launcher", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private async void ExitApplication()
    {
        if (_exiting)
        {
            return;
        }

        _exiting = true;
        _controlCancellation.Cancel();
        try
        {
            await _supervisor.StopAsync();
        }
        catch (Exception exception)
        {
            _logger.Error("Could not stop Harness while exiting", exception);
        }

        _supervisor.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
        _controlCancellation.Dispose();
        _logger.Info("Tray instance stopped.");
        _logger.Dispose();
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_exiting)
        {
            ExitApplication();
        }

        base.Dispose(disposing);
    }
}
