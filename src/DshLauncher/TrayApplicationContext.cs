using System.IO.Pipes;
using System.Threading;
using System.Windows.Forms;

namespace DshLauncher;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private const string CheckUpdatesText = "检查更新 / Check for updates";
    private const string AutoCheckUpdatesText = "自动检查更新 / Auto-check updates";
    private readonly LauncherConfig _config;
    private readonly LauncherLogger _logger;
    private readonly ProcessSupervisor _supervisor;
    private readonly StartCoordinator _startCoordinator;
    private readonly Icon _applicationIcon;
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu;
    private readonly SynchronizationContext _uiContext;
    private readonly CancellationTokenSource _controlCancellation = new();
    private readonly UpdateChecker _updateChecker;
    private readonly UpdatePreferencesStore _updatePreferencesStore;
    private StartupSplashForm? _startupSplash;
    private SponsorForm? _sponsorForm;
    private ToolStripMenuItem? _checkUpdatesMenuItem;
    private ToolStripMenuItem? _autoCheckUpdatesMenuItem;
    private Task<StartResult>? _splashOperation;
    private UpdatePreferences _updatePreferences;
    private Uri? _availableUpdateUri;
    private bool _checkingUpdates;
    private bool _exiting;
    private string _status = "starting";

    public TrayApplicationContext(LauncherConfig config, LauncherLogger logger, bool openBrowserOnStart)
    {
        _config = config;
        _logger = logger;
        _uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _supervisor = new ProcessSupervisor(config, logger);
        _supervisor.ProcessExited += HandleProcessExited;
        _startCoordinator = new StartCoordinator(
            () => _supervisor.StartAsync(),
            OpenBrowser);
        _updateChecker = new UpdateChecker();
        _updatePreferencesStore = UpdatePreferencesStore.CreateDefault();
        _updatePreferences = _updatePreferencesStore.Load();

        _menu = CreateMenu();
        _applicationIcon = LoadApplicationIcon();
        _notifyIcon = new NotifyIcon
        {
            Icon = _applicationIcon,
            Text = "dsh-launcher: starting",
            Visible = true,
            ContextMenuStrip = _menu
        };
        _notifyIcon.DoubleClick += HandleOpenClick;
        _notifyIcon.BalloonTipClicked += HandleUpdateNotificationClick;

        _logger.Info($"Tray instance started. Web URL: {_config.WebUrl}.");
        _ = RunControlServerAsync();
        _ = StartAndOpenAsync(openBrowserOnStart, showErrors: true);
        _ = RunAutomaticUpdateCheckAsync();
    }

    private ContextMenuStrip CreateMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(CreateMenuItem("启动 / Start", async (_, _) => await StartAndOpenAsync(_config.AutoOpen, true)));
        menu.Items.Add(CreateMenuItem("打开网页 / Open web", HandleOpenClick));
        menu.Items.Add(CreateMenuItem("查看状态 / Status", HandleStatusClick));
        menu.Items.Add(CreateMenuItem("重启 / Restart", async (_, _) => await RestartAsync()));
        menu.Items.Add(CreateMenuItem("停止 / Stop", async (_, _) => await StopAsync()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(CreateMenuItem("打开日志目录 / Open logs", HandleLogsClick));
        menu.Items.Add(CreateMenuItem("复制诊断报告 / Copy diagnostics", async (_, _) => await CopyDiagnosticsAsync()));
        menu.Items.Add(new ToolStripSeparator());
        _checkUpdatesMenuItem = CreateMenuItem(CheckUpdatesText, HandleCheckUpdatesClick);
        menu.Items.Add(_checkUpdatesMenuItem);
        _autoCheckUpdatesMenuItem = new ToolStripMenuItem(AutoCheckUpdatesText)
        {
            Checked = _updatePreferences.AutoCheckUpdates,
            CheckOnClick = true,
        };
        _autoCheckUpdatesMenuItem.CheckedChanged += HandleAutoCheckUpdatesChanged;
        menu.Items.Add(_autoCheckUpdatesMenuItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(CreateMenuItem("赞赏作者 / Support", HandleSponsorClick));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(CreateMenuItem("退出 / Exit", (_, _) => ExitApplication()));
        return menu;
    }

    private static ToolStripMenuItem CreateMenuItem(string text, EventHandler handler)
    {
        var item = new ToolStripMenuItem(text);
        item.Click += handler;
        return item;
    }

    private static Icon LoadApplicationIcon()
    {
        using var stream = typeof(TrayApplicationContext).Assembly.GetManifestResourceStream(
            "DshLauncher.Assets.dsh-launcher.ico");
        if (stream is null)
        {
            return (Icon)SystemIcons.Application.Clone();
        }

        using var icon = new Icon(stream);
        return (Icon)icon.Clone();
    }

    private async void HandleOpenClick(object? sender, EventArgs args)
    {
        await StartAndOpenAsync(openBrowser: true, showErrors: true);
    }

    private void HandleStatusClick(object? sender, EventArgs args)
    {
        ShowStatus(BuildStatusMessage());
    }

    private void HandleSponsorClick(object? sender, EventArgs args)
    {
        if (_sponsorForm is null || _sponsorForm.IsDisposed)
        {
            _sponsorForm = new SponsorForm(_applicationIcon);
            _sponsorForm.FormClosed += HandleSponsorClosed;
            _sponsorForm.Show();
            return;
        }

        if (_sponsorForm.WindowState == FormWindowState.Minimized)
        {
            _sponsorForm.WindowState = FormWindowState.Normal;
        }
        _sponsorForm.Activate();
    }

    private void HandleSponsorClosed(object? sender, FormClosedEventArgs args)
    {
        _sponsorForm = null;
    }

    private async void HandleCheckUpdatesClick(object? sender, EventArgs args)
    {
        await CheckForUpdatesAsync(manual: true);
    }

    private void HandleAutoCheckUpdatesChanged(object? sender, EventArgs args)
    {
        if (_autoCheckUpdatesMenuItem is null)
        {
            return;
        }

        _updatePreferences = _updatePreferences with
        {
            AutoCheckUpdates = _autoCheckUpdatesMenuItem.Checked,
        };
        SaveUpdatePreferences();
        if (_updatePreferences.AutoCheckUpdates)
        {
            _ = CheckForUpdatesAsync(manual: false);
        }
    }

    private async Task RunAutomaticUpdateCheckAsync()
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(10), _controlCancellation.Token);
            var now = DateTimeOffset.UtcNow;
            if (!_updatePreferences.AutoCheckUpdates ||
                _updatePreferences.LastUpdateCheckUtc is { } checkedAt &&
                checkedAt <= now && now - checkedAt < TimeSpan.FromHours(24))
            {
                return;
            }

            await CheckForUpdatesAsync(manual: false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task CheckForUpdatesAsync(bool manual)
    {
        if (_checkingUpdates)
        {
            if (manual)
            {
                ShowStatus("正在检查更新，请稍候。\n\nAn update check is already running.");
            }
            return;
        }

        _checkingUpdates = true;
        if (_checkUpdatesMenuItem is not null)
        {
            _checkUpdatesMenuItem.Enabled = false;
            _checkUpdatesMenuItem.Text = "正在检查更新... / Checking for updates...";
        }

        try
        {
            var result = await _updateChecker.CheckAsync(_controlCancellation.Token);
            _updatePreferences = _updatePreferences with { LastUpdateCheckUtc = DateTimeOffset.UtcNow };
            SaveUpdatePreferences();
            if (!manual && !_updatePreferences.AutoCheckUpdates)
            {
                return;
            }

            if (!result.IsUpdateAvailable)
            {
                _availableUpdateUri = null;
                if (manual)
                {
                    ShowStatus($"当前已是最新版：v{result.CurrentVersion}\n\nYou are up to date.");
                }
                return;
            }

            _availableUpdateUri = result.ReleaseUri;
            if (_checkUpdatesMenuItem is not null)
            {
                _checkUpdatesMenuItem.Text = $"发现 {result.LatestTag} / Update available";
            }

            if (manual)
            {
                ShowUpdatePrompt(result);
            }
            else
            {
                _notifyIcon.BalloonTipTitle = "dsh-launcher 有新版本 / Update available";
                _notifyIcon.BalloonTipText = $"{result.LatestTag} 已发布。点击查看下载页面。";
                _notifyIcon.ShowBalloonTip(8000);
            }
        }
        catch (OperationCanceledException) when (_exiting)
        {
        }
        catch (Exception exception)
        {
            _logger.Error("Could not check for launcher updates", exception);
            if (manual)
            {
                ShowError($"检查更新失败 / Could not check for updates:\n{exception.Message}");
            }
        }
        finally
        {
            _checkingUpdates = false;
            if (_checkUpdatesMenuItem is not null && !_exiting)
            {
                _checkUpdatesMenuItem.Enabled = true;
                if (_availableUpdateUri is null)
                {
                    _checkUpdatesMenuItem.Text = CheckUpdatesText;
                }
            }
        }
    }

    private void ShowUpdatePrompt(UpdateCheckResult result)
    {
        var answer = MessageBox.Show(
            $"发现新版本 {result.LatestTag}，当前版本为 v{result.CurrentVersion}。\n\n" +
            "是否打开 GitHub Release 下载页面？\n\n" +
            "A new version is available. Open the download page?",
            "dsh-launcher",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Information);
        if (answer == DialogResult.Yes)
        {
            OpenUpdatePage(result.ReleaseUri);
        }
    }

    private void HandleUpdateNotificationClick(object? sender, EventArgs args)
    {
        if (_availableUpdateUri is not null)
        {
            OpenUpdatePage(_availableUpdateUri);
        }
    }

    private void OpenUpdatePage(Uri releaseUri)
    {
        try
        {
            BrowserLauncher.Open(releaseUri);
        }
        catch (Exception exception)
        {
            _logger.Error("Could not open the launcher update page", exception);
            ShowError($"无法打开更新页面 / Could not open the update page:\n{exception.Message}");
        }
    }

    private void SaveUpdatePreferences()
    {
        try
        {
            _updatePreferencesStore.Save(_updatePreferences);
        }
        catch (Exception exception)
        {
            _logger.Error("Could not save update preferences", exception);
        }
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

    private async Task CopyDiagnosticsAsync()
    {
        try
        {
            var report = await new DoctorRunner(_config, _logger).RunAsync();
            Clipboard.SetText(report.ToText());
            ShowStatus(report.HasFailures
                ? "诊断报告已复制；发现需要处理的问题。\n\nDiagnostics copied; problems were found."
                : "诊断报告已复制。\n\nDiagnostics copied.");
        }
        catch (Exception exception)
        {
            _logger.Error("Could not create diagnostics", exception);
            ShowError($"无法生成诊断报告 / Could not create diagnostics:\n{exception.Message}");
        }
    }

    private async Task<string> StartAndOpenAsync(bool openBrowser, bool showErrors)
    {
        var request = _startCoordinator.Request(openBrowser);
        if (openBrowser)
        {
            ShowStartupSplash(request.Completion);
        }

        if (request.IsOwner)
        {
            SetStatus("starting");
        }

        try
        {
            var result = await request.Completion;
            CloseStartupSplash(request.Completion);

            if (request.IsOwner && result.Exited)
            {
                SetStatus("stopped");
                if (showErrors)
                {
                    ShowError(result.Message);
                }
            }
            else if (request.IsOwner && result.Ready)
            {
                SetStatus("running");
            }
            else if (request.IsOwner)
            {
                SetStatus("starting");
            }

            return result.Message;
        }
        catch (OperationCanceledException)
        {
            CloseStartupSplash(request.Completion);
            if (request.IsOwner)
            {
                SetStatus("stopped");
            }

            return "Start cancelled.";
        }
        catch (Exception exception)
        {
            CloseStartupSplash(request.Completion);
            _logger.Error("Could not start DeepSeek Harness", exception);
            if (request.IsOwner)
            {
                SetStatus("error");
                if (showErrors)
                {
                    ShowError($"启动 DeepSeek Harness 失败 / Could not start DeepSeek Harness:\n{exception.Message}");
                }
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
            "start" or "activate" => QueueStart(_config.AutoOpen),
            "open" or "activate-open" => QueueStart(openBrowser: true),
            "restart" => await RestartFromControlAsync(),
            "stop" => await StopFromControlAsync(),
            "exit" => QueueExit(),
            "status" => BuildStatusMessage().Replace(Environment.NewLine, " | "),
            "logs" => OpenLogsFromControl(),
            _ => $"Unknown launcher command: {command}"
        };
    }

    private void ShowStartupSplash(Task<StartResult> operation)
    {
        if (_exiting)
        {
            return;
        }

        _splashOperation = operation;
        if (_startupSplash is null || _startupSplash.IsDisposed)
        {
            _startupSplash = new StartupSplashForm(_applicationIcon);
            _startupSplash.Show();
            return;
        }

        if (!_startupSplash.Visible)
        {
            _startupSplash.Show();
        }

        _startupSplash.Activate();
    }

    private void CloseStartupSplash(Task<StartResult>? operation = null)
    {
        if (operation is not null && !ReferenceEquals(_splashOperation, operation))
        {
            return;
        }

        _splashOperation = null;
        var splash = _startupSplash;
        _startupSplash = null;
        if (splash is null || splash.IsDisposed)
        {
            return;
        }

        splash.Close();
        splash.Dispose();
    }

    private string QueueStart(bool openBrowser)
    {
        _ = StartAndOpenAsync(openBrowser, showErrors: false);
        return openBrowser
            ? $"DeepSeek Harness will open when {_config.WebUrl} is ready."
            : "DeepSeek Harness start requested.";
    }

    private string QueueExit()
    {
        _ = ExitAfterControlResponseAsync();
        return "Tray exit requested.";
    }

    private async Task ExitAfterControlResponseAsync()
    {
        await Task.Delay(200).ConfigureAwait(false);
        _uiContext.Post(_ => ExitApplication(), null);
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

    private void OpenBrowser()
    {
        try
        {
            BrowserLauncher.Open(_config.WebUrl);
            _logger.Info($"Opened web browser at {_config.WebUrl}.");
        }
        catch (Exception exception)
        {
            _logger.Error("Could not open the web browser", exception);
            ShowError($"无法打开浏览器 / Could not open the browser:\n{exception.Message}");
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
        CloseStartupSplash();
        CloseSponsorForm();
        try
        {
            await _supervisor.StopAsync();
        }
        catch (Exception exception)
        {
            _logger.Error("Could not stop Harness while exiting", exception);
        }

        _supervisor.Dispose();
        _updateChecker.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.BalloonTipClicked -= HandleUpdateNotificationClick;
        _notifyIcon.Dispose();
        _applicationIcon.Dispose();
        _menu.Dispose();
        _controlCancellation.Dispose();
        _logger.Info("Tray instance stopped.");
        _logger.Dispose();
        ExitThread();
    }

    private void CloseSponsorForm()
    {
        var form = _sponsorForm;
        _sponsorForm = null;
        if (form is null)
        {
            return;
        }

        form.FormClosed -= HandleSponsorClosed;
        form.Close();
        form.Dispose();
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
