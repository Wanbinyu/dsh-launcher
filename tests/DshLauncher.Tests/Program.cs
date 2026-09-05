using System.Net;
using System.Net.Sockets;
using System.Text;
using DshLauncher;

if (args.Contains("--preview-sponsor", StringComparer.Ordinal))
{
    System.Windows.Forms.Application.EnableVisualStyles();
    System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
    System.Windows.Forms.Application.Run(new SponsorForm(System.Drawing.SystemIcons.Application));
    return;
}

var runner = new RunnerSpec(
    "npx.cmd",
    new[] { "--yes", "@deepseek-ai/dsh" },
    Environment.CurrentDirectory,
    "test runner");

var webRunner = ProcessSupervisor.AddWebCommand(runner);
var expectedArguments = new[] { "--yes", "@deepseek-ai/dsh", "web" };
if (!webRunner.PrefixArguments.SequenceEqual(expectedArguments))
{
    throw new InvalidOperationException(
        $"Expected '{string.Join(' ', expectedArguments)}', got '{string.Join(' ', webRunner.PrefixArguments)}'.");
}

if (!runner.PrefixArguments.SequenceEqual(expectedArguments[..^1]))
{
    throw new InvalidOperationException("Preparing the web runner mutated the original runner.");
}

var rc8Runner = ProcessSupervisor.AddWebCommand(runner with { DshVersion = "0.1.0-rc.8" });
var expectedRc8Arguments = new[] { "--yes", "@deepseek-ai/dsh", "web", "--no-open" };
if (!rc8Runner.PrefixArguments.SequenceEqual(expectedRc8Arguments))
{
    throw new InvalidOperationException(
        $"Expected '{string.Join(' ', expectedRc8Arguments)}', got '{string.Join(' ', rc8Runner.PrefixArguments)}'.");
}

foreach (var version in new[] { "0.1.0-rc.8", "0.1.0-rc.9", "0.1.0", "0.1.1-rc.1", "0.1.1", "1.0.0" })
{
    if (!ProcessSupervisor.SupportsNoOpen(version))
    {
        throw new InvalidOperationException($"Expected Harness {version} to support --no-open.");
    }
}

foreach (var version in new string?[] { null, "", "0.1.0-rc.7", "0.0.9", "invalid" })
{
    if (ProcessSupervisor.SupportsNoOpen(version))
    {
        throw new InvalidOperationException($"Did not expect Harness {version} to support --no-open.");
    }
}

await VerifyNpxFallbackAsync();
await VerifyLocalPackageVersionAsync();
await VerifyManagedHarnessResolutionAsync();
await VerifyManagedRemovalPreservesUnknownFilesAsync();
await VerifyStartRequestsAreCoalescedAsync();
VerifyDshWebLaunchUrlParsing();
await VerifyAuthenticatedWebLaunchAsync();
await VerifyStartupTimeoutAsync();
await VerifyWebHealthChecksAsync();
VerifyIconResource();
VerifyStartupSplash();
VerifyHarnessSetupWindows();
VerifyManagedInstallCommands();
VerifySponsorWindow();
VerifyUpdateParsing();
VerifyUpdatePreferences();
VerifyRecommendationPreferences();
VerifyRecommendationCatalog();
await VerifyRecommendationSourceHealthAsync();
VerifyRecommendationWindow();
Console.WriteLine("DshLauncher tests passed.");

static void VerifyUpdateParsing()
{
    var available = UpdateChecker.ParseLatestRelease(
        "{\"tag_name\":\"v0.5.1\"}",
        new Version(0, 5, 0, 0));
    if (!available.IsUpdateAvailable || available.LatestVersion != new Version(0, 5, 1) ||
        available.ReleaseUri.AbsoluteUri != "https://github.com/Wanbinyu/dsh-launcher/releases/tag/v0.5.1")
    {
        throw new InvalidOperationException("A newer launcher release was not detected correctly.");
    }

    var current = UpdateChecker.ParseLatestRelease(
        "{\"tag_name\":\"0.5.0\"}",
        new Version(0, 5, 0, 0));
    if (current.IsUpdateAvailable)
    {
        throw new InvalidOperationException("The current launcher release was reported as newer.");
    }

    try
    {
        UpdateChecker.ParseLatestRelease("{\"tag_name\":\"not-a-version\"}", new Version(0, 5, 0));
        throw new InvalidOperationException("An invalid launcher release tag was accepted.");
    }
    catch (InvalidDataException)
    {
    }
}

static void VerifyUpdatePreferences()
{
    var directory = Path.Combine(Path.GetTempPath(), $"dsh-launcher-preferences-{Guid.NewGuid():N}");
    var path = Path.Combine(directory, "preferences.json");
    try
    {
        var store = new UpdatePreferencesStore(path);
        if (!store.Load().AutoCheckUpdates)
        {
            throw new InvalidOperationException("Automatic update checks should default to enabled.");
        }

        var checkedAt = DateTimeOffset.UtcNow.AddMinutes(-3);
        store.Save(new UpdatePreferences(AutoCheckUpdates: false, LastUpdateCheckUtc: checkedAt));
        var saved = store.Load();
        if (saved.AutoCheckUpdates || saved.LastUpdateCheckUtc != checkedAt)
        {
            throw new InvalidOperationException("Update preferences did not round-trip.");
        }

        File.WriteAllText(path, "invalid json");
        if (!store.Load().AutoCheckUpdates)
        {
            throw new InvalidOperationException("Invalid update preferences did not use safe defaults.");
        }
    }
    finally
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}

static void VerifyRecommendationPreferences()
{
    var directory = Path.Combine(Path.GetTempPath(), $"dsh-launcher-recommendations-{Guid.NewGuid():N}");
    var path = Path.Combine(directory, "recommendations.json");
    try
    {
        var store = new RecommendationPreferencesStore(path);
        var initial = store.Load();
        if (!initial.NeedsPrompt("0.5.0") || initial.SelectedProfileId is not null)
        {
            throw new InvalidOperationException("Recommendation preferences did not use first-run defaults.");
        }

        store.Save(new RecommendationPreferences(
            LastPromptedVersion: "0.5.0",
            SelectedProfileId: "ai-cost"));
        var saved = store.Load();
        if (saved.NeedsPrompt("0.5.0") ||
            !saved.NeedsPrompt("0.5.1") ||
            saved.SelectedProfileId != "ai-cost")
        {
            throw new InvalidOperationException("Recommendation prompt version or profile did not round-trip.");
        }

        File.WriteAllText(path, "invalid json");
        if (!store.Load().NeedsPrompt("0.5.0"))
        {
            throw new InvalidOperationException("Invalid recommendation preferences did not use safe defaults.");
        }
    }
    finally
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}

static void VerifyRecommendationCatalog()
{
    var catalog = PluginRecommendationCatalog.LoadEmbedded();
    if (catalog.Profiles.Count != 10 ||
        catalog.Items.Count != 19 ||
        catalog.Plugins.Count != 11 ||
        catalog.Skills.Count != 8 ||
        catalog.ForProfile("office").Count != 6 ||
        catalog.ForProfile("software").Count != 6 ||
        catalog.ForProfile("complete").Count != 19 ||
        catalog.Plugins.Any(plugin =>
            !plugin.InstallCommand.StartsWith("dsh plugin --profile web add ", StringComparison.Ordinal)) ||
        catalog.Skills.Any(skill =>
            !skill.InstallCommand.StartsWith("$env:DO_NOT_TRACK='1'; npx -y skills add ", StringComparison.Ordinal) ||
            !skill.InstallCommand.EndsWith(" -a universal --copy -y", StringComparison.Ordinal)) ||
        !catalog.Items.Any(item => item.Publisher == "Anthropic") ||
        !catalog.Items.Any(item => item.Publisher == "PensiveFei") ||
        !catalog.Items.Any(item => item.Publisher == "Wanbinyu"))
    {
        throw new InvalidOperationException("The embedded plugin and Skills recommendation catalog is incomplete.");
    }

    if (catalog.Profiles
        .Where(profile => profile.Id != "complete")
        .Any(profile => catalog.ForProfile(profile.Id).Count is < 3 or > 6))
    {
        throw new InvalidOperationException("A normal workflow should recommend only three to six items.");
    }

    var installationRequest = PluginRecommendationForm.BuildInstallationRequest(new[]
    {
        catalog.Skills[0],
        catalog.Plugins[0],
    });
    if (!installationRequest.StartsWith("请帮我安装下面选中的 DeepSeek Harness 插件和 Skills。", StringComparison.Ordinal) ||
        !installationRequest.Contains("DO_NOT_TRACK=1", StringComparison.Ordinal) ||
        !installationRequest.Contains("不要读取或修改 API Key", StringComparison.Ordinal) ||
        !installationRequest.Contains("命令：dsh plugin --profile web add", StringComparison.Ordinal) ||
        installationRequest.Contains("dsh restart", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("The generated Harness installation request is incomplete or unsafe.");
    }

    string? copiedRequest = null;
    var harnessOpened = false;
    PluginRecommendationForm.CopyInstallationRequest(
        installationRequest,
        text => copiedRequest = text,
        () => harnessOpened = true);
    if (copiedRequest != installationRequest || !harnessOpened)
    {
        throw new InvalidOperationException("Copying the request did not hand off to Harness.");
    }

    try
    {
        PluginRecommendationCatalog.Parse("{\"schemaVersion\":1,\"profiles\":[],\"items\":[]}");
        throw new InvalidOperationException("An unsupported recommendation schema was accepted.");
    }
    catch (InvalidDataException)
    {
    }
}

static void VerifyRecommendationWindow()
{
    var catalog = PluginRecommendationCatalog.LoadEmbedded();
    string? selectedProfile = null;
    var openHarnessInvoked = false;
    using var form = new PluginRecommendationForm(
        catalog,
        "office",
        System.Drawing.SystemIcons.Application,
        profile => selectedProfile = profile,
        () => openHarnessInvoked = true);
    var controls = Descendants(form).ToArray();
    if (form.MaximizeBox || form.MinimizeBox ||
        form.SelectedProfileId != "office" ||
        selectedProfile != "office" ||
        openHarnessInvoked ||
        form.VisibleItemCount != 6 ||
        form.CheckedItemCount != 6 ||
        controls.OfType<System.Windows.Forms.ComboBox>()
            .SingleOrDefault(combo => combo.AccessibleName == "使用方向 / Workflow")?.Items.Count != 10 ||
        !form.SelectedItemDetails.Contains(catalog.ForProfile("office")[0].DescriptionZh, StringComparison.Ordinal) ||
        !form.SelectedItemDetails.Contains(catalog.ForProfile("office")[0].DescriptionEn, StringComparison.Ordinal) ||
        !form.InstallationRequestPreview.Contains("请帮我安装", StringComparison.Ordinal) ||
        !form.InstallationRequestPreview.Contains("npx -y skills add", StringComparison.Ordinal) ||
        !form.InstallationRequestPreview.Contains("dsh plugin --profile web add", StringComparison.Ordinal) ||
        !controls.OfType<System.Windows.Forms.Label>().Any(label =>
            label.Text.Contains("不会读取会话、文件或密钥", StringComparison.Ordinal)) ||
        !controls.OfType<System.Windows.Forms.Button>().Any(button =>
            button.Text.Contains("复制安装请求并打开 Harness", StringComparison.Ordinal)))
    {
        throw new InvalidOperationException("The plugin and Skills recommendation window is incomplete.");
    }

    form.SetSearchTextForTest("Excel");
    if (form.VisibleItemCount != 1)
    {
        throw new InvalidOperationException("Recommendation search did not match bilingual descriptions.");
    }

    form.SetSearchTextForTest(string.Empty);
    form.SetKindFilterForTest("skill");
    if (form.VisibleItemCount != 3)
    {
        throw new InvalidOperationException("The Skill filter returned an unexpected office catalog.");
    }

    form.SetKindFilterForTest("all");
    form.SetLicenseFilterForTest("open");
    if (form.VisibleItemCount != 3)
    {
        throw new InvalidOperationException("The open-source filter returned an unexpected office catalog.");
    }

    form.SetLicenseFilterForTest("all");
    var automation = catalog.Items.Single(item => item.Id == "dsh-automation");
    form.ApplyInstallStatusesForTest(new Dictionary<string, RecommendationInstallStatus>
    {
        [automation.Id] = new(
            RecommendationInstallState.InstalledCurrent,
            automation.Version),
    });
    form.SetHideInstalledForTest(true);
    if (form.VisibleItemCount != 5 || form.CheckedItemCount != 5 ||
        form.InstallationRequestPreview.Contains(automation.InstallCommand, StringComparison.Ordinal))
    {
        throw new InvalidOperationException("Installed plugins were not removed from the filtered request.");
    }

    using var completeForm = new PluginRecommendationForm(
        catalog,
        "complete",
        System.Drawing.SystemIcons.Application,
        _ => { },
        () => { });
    if (completeForm.VisibleItemCount != 19 || completeForm.CheckedItemCount != 0 ||
        !completeForm.InstallationRequestPreview.Contains("默认不勾选", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("The full catalog should be visible without selecting every item.");
    }
}

static async Task VerifyRecommendationSourceHealthAsync()
{
    var catalog = PluginRecommendationCatalog.LoadEmbedded();
    var billing = catalog.Items.Single(item => item.Id == "dsh-billing");
    if (billing.PackageNameForInspection != "dsh-billing-community-bundle")
    {
        throw new InvalidOperationException("The billing bundle inspection name is incorrect.");
    }

    var installed = RecommendationInstallInspector.ParseInstalledPackages(
        "[{\"dependencies\":{\"dsh-billing-community-bundle\":{\"version\":\"0.6.3\"}," +
        "\"dsh-error-lens\":{\"version\":\"0.1.2\"}}}]");
    if (installed["dsh-billing-community-bundle"] != "0.6.3" ||
        installed["dsh-error-lens"] != "0.1.2")
    {
        throw new InvalidOperationException("Installed plugin list parsing is incorrect.");
    }

    var promptPresets = catalog.Items.Single(item => item.Id == "dsh-prompt-presets");
    var manifestUri = RecommendationSourceHealthChecker.TryBuildManifestUri(promptPresets.RepositoryUrl);
    var installUri = RecommendationSourceHealthChecker.BuildInstallSourceUri(promptPresets);
    var patchUri = manifestUri is null
        ? null
        : RecommendationSourceHealthChecker.TryBuildBundlePatchUri(
            manifestUri,
            "./cordis.patch.yml");
    if (manifestUri?.AbsoluteUri !=
            "https://raw.githubusercontent.com/zhangdong456/dsh-prompt-presets/HEAD/package.json" ||
        patchUri?.AbsoluteUri !=
            "https://raw.githubusercontent.com/zhangdong456/dsh-prompt-presets/HEAD/cordis.patch.yml" ||
        installUri.AbsoluteUri != "https://registry.npmjs.org/dsh-prompt-presets/1.0.4" ||
        !RecommendationSourceHealthChecker.PackageDeclaresDshBundle(
            "{\"dsh\":{\"bundle\":{\"patch\":\"./cordis.patch.yml\"}}}") ||
        RecommendationSourceHealthChecker.PackageDeclaresDshBundle("{\"dsh\":{}}"))
    {
        throw new InvalidOperationException("Recommendation source validation helpers are incorrect.");
    }

    var xlsx = catalog.Items.Single(item => item.Id == "skill-xlsx");
    using var client = new HttpClient(new RecommendationHealthStubHandler(request =>
    {
        var body = request.RequestUri == manifestUri
            ? "{\"dsh\":{\"bundle\":{\"patch\":\"./cordis.patch.yml\"}}}"
            : "{}";
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body),
        };
    }));
    using var checker = new RecommendationSourceHealthChecker(client);
    var health = await checker.CheckAsync(new[] { promptPresets, xlsx });
    if (health[promptPresets.Id].State != RecommendationSourceHealthState.Available ||
        health[xlsx.Id].State != RecommendationSourceHealthState.Available)
    {
        throw new InvalidOperationException("Healthy recommendation sources were not recognized.");
    }
}

static void VerifySponsorWindow()
{
    using var form = new SponsorForm(System.Drawing.SystemIcons.Application);
    if (form.MaximizeBox || form.MinimizeBox || form.TopMost)
    {
        throw new InvalidOperationException("The support window has unexpected window behavior.");
    }

    var controls = Descendants(form).ToArray();
    var tabs = controls.OfType<System.Windows.Forms.TabControl>().SingleOrDefault();
    var codes = controls.OfType<System.Windows.Forms.PictureBox>().ToArray();
    if (tabs?.TabPages.Count != 2 || codes.Length != 2 || codes.Any(code => code.Image is null) ||
        !controls.OfType<System.Windows.Forms.Label>().Any(label =>
            label.Text.Contains("完全自愿，不影响任何功能", StringComparison.Ordinal)))
    {
        throw new InvalidOperationException("The support window is missing its voluntary support UI.");
    }
}

static void VerifyStartupSplash()
{
    using var form = new StartupSplashForm(System.Drawing.SystemIcons.Application);
    if (form.ShowInTaskbar || !form.TopMost || form.MaximizeBox || form.MinimizeBox)
    {
        throw new InvalidOperationException("The startup progress window has unexpected window behavior.");
    }

    var controls = Descendants(form).ToArray();
    var progress = controls.OfType<System.Windows.Forms.ProgressBar>().SingleOrDefault();
    if (progress?.Style != System.Windows.Forms.ProgressBarStyle.Marquee ||
        !controls.OfType<System.Windows.Forms.Label>().Any(label =>
            label.Text.Contains("DeepSeek Harness 正在启动", StringComparison.Ordinal)))
    {
        throw new InvalidOperationException("The startup progress window is missing its progress UI.");
    }
}

static void VerifyHarnessSetupWindows()
{
    var assessment = new HarnessEnvironmentAssessment(
        ExistingRunner: null,
        NodePath: @"C:\Program Files\nodejs\node.exe",
        NodeVersion: new Version(24, 0, 0),
        NpmPath: @"C:\Program Files\nodejs\npm.cmd",
        NpxPath: @"C:\Program Files\nodejs\npx.cmd",
        WingetPath: @"C:\Windows\winget.exe");
    using var prompt = new HarnessSetupPromptForm(assessment, System.Drawing.SystemIcons.Application);
    var promptControls = Descendants(prompt).ToArray();
    if (!prompt.TopMost || prompt.MaximizeBox || prompt.MinimizeBox ||
        !promptControls.OfType<System.Windows.Forms.Label>().Any(label =>
            label.Text.Contains("首次配置 DeepSeek Harness", StringComparison.Ordinal)) ||
        !promptControls.OfType<System.Windows.Forms.Button>().Any(button =>
            button.Text.Contains("安装 Harness 并启动", StringComparison.Ordinal)))
    {
        throw new InvalidOperationException("The Harness setup prompt is missing its first-run installation UI.");
    }

    using var progress = new HarnessInstallProgressForm(System.Drawing.SystemIcons.Application);
    progress.Report(new HarnessInstallProgress("测试安装阶段", "测试安装详情"));
    var progressControls = Descendants(progress).ToArray();
    if (!progress.TopMost ||
        progressControls.OfType<System.Windows.Forms.ProgressBar>().SingleOrDefault()?.Style !=
            System.Windows.Forms.ProgressBarStyle.Marquee ||
        !progressControls.OfType<System.Windows.Forms.Label>().Any(label =>
            label.Text.Contains("测试安装阶段", StringComparison.Ordinal)))
    {
        throw new InvalidOperationException("The Harness installation progress window is incomplete.");
    }

    progress.MarkCompleted();
}

static void VerifyManagedInstallCommands()
{
    if (ManagedHarnessInstaller.ParseNodeVersion("v22.19.0") != new Version(22, 19, 0) ||
        ManagedHarnessInstaller.ParseNodeVersion("invalid") is not null ||
        !(new HarnessEnvironmentAssessment(
            ExistingRunner: null,
            NodePath: "node.exe",
            NodeVersion: new Version(22, 18, 0),
            NpmPath: "npm.cmd",
            NpxPath: "npx.cmd",
            WingetPath: "winget.exe")).HasNodeAndNpm ||
        (new HarnessEnvironmentAssessment(
            ExistingRunner: null,
            NodePath: "node.exe",
            NodeVersion: new Version(22, 18, 0),
            NpmPath: "npm.cmd",
            NpxPath: "npx.cmd",
            WingetPath: "winget.exe")).HasCompatibleNodeAndNpm)
    {
        throw new InvalidOperationException("Node.js compatibility detection is incorrect.");
    }

    if (!ManagedHarnessInstaller.AllowedBuildDependencies.SequenceEqual(new[]
        {
            "@deepseek-ai/dsh-subprocess-local", "@google/genai", "koffi", "node-pty", "protobufjs",
        }))
    {
        throw new InvalidOperationException("The managed Harness build-script allowlist changed unexpectedly.");
    }
    var workspaceConfig = ManagedHarnessInstaller.BuildPnpmWorkspaceConfig();
    if (!workspaceConfig.StartsWith("allowBuilds:", StringComparison.Ordinal) ||
        ManagedHarnessInstaller.AllowedBuildDependencies.Any(package =>
            !workspaceConfig.Contains($"  '{package}': true", StringComparison.Ordinal)))
    {
        throw new InvalidOperationException("The pnpm workspace build-script allowlist is incomplete.");
    }

    var winget = ManagedHarnessInstaller.BuildWingetInstallArguments();
    if (!winget.SequenceEqual(new[]
        {
            "install", "--id", "OpenJS.NodeJS.LTS", "--exact", "--source", "winget",
            "--accept-package-agreements", "--accept-source-agreements", "--silent", "--disable-interactivity",
        }))
    {
        throw new InvalidOperationException("The Node.js winget command is not deterministic and unattended.");
    }

    var installRoot = Path.Combine(Path.GetTempPath(), "dsh launcher managed test");
    var npm = ManagedHarnessInstaller.BuildPnpmBootstrapArguments(Path.Combine(installRoot, ".tools"));
    if (!npm.Contains("--save-exact") || !npm.Contains("--no-audit") || !npm.Contains("--no-fund") ||
        !npm.Contains("pnpm@11.24.0") ||
        !npm.Contains(Path.GetFullPath(Path.Combine(installRoot, ".tools"))))
    {
        throw new InvalidOperationException("The managed pnpm bootstrap command is incomplete.");
    }

    var pnpmEntry = Path.Combine(installRoot, ".tools", "node_modules", "pnpm", "bin", "pnpm.cjs");
    var pnpm = ManagedHarnessInstaller.BuildPnpmInstallArguments(pnpmEntry, "0.1.1-rc.2");
    if (!pnpm.Contains(Path.GetFullPath(pnpmEntry)) || !pnpm.Contains("--save-exact") ||
        pnpm.Contains("--ignore-workspace") || !pnpm.Contains("--reporter=append-only") ||
        !pnpm.Contains("@deepseek-ai/dsh@0.1.1-rc.2"))
    {
        throw new InvalidOperationException("The managed Harness pnpm command is incomplete.");
    }
    var rebuild = ManagedHarnessInstaller.BuildPnpmRebuildArguments(pnpmEntry);
    if (!rebuild.SequenceEqual(new[] { Path.GetFullPath(pnpmEntry), "rebuild", "--reporter=append-only" }))
    {
        throw new InvalidOperationException("The managed Harness repair command is incomplete.");
    }
}

static IEnumerable<System.Windows.Forms.Control> Descendants(System.Windows.Forms.Control parent)
{
    foreach (System.Windows.Forms.Control child in parent.Controls)
    {
        yield return child;
        foreach (var descendant in Descendants(child))
        {
            yield return descendant;
        }
    }
}

static async Task VerifyWebHealthChecksAsync()
{
    foreach (var (statusCode, expectedResponding, expectedRequiresAuthentication) in new[]
    {
        (200, true, false),
        (302, true, false),
        (401, false, true),
        (404, false, false),
        (500, false, false),
    })
    {
        var result = await ProbeStatusAsync(statusCode);
        if (result.Responding != expectedResponding ||
            result.RequiresAuthentication != expectedRequiresAuthentication ||
            result.StatusCode != statusCode)
        {
            throw new InvalidOperationException(
                $"HTTP {statusCode} readiness was {result.Responding}/{result.RequiresAuthentication}; " +
                $"expected {expectedResponding}/{expectedRequiresAuthentication}.");
        }
    }
}

static async Task<WebHealthChecker.ProbeResult> ProbeStatusAsync(int statusCode)
{
    var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    try
    {
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        var responseTask = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            await using var stream = client.GetStream();
            var request = new byte[4096];
            _ = await stream.ReadAsync(request);
            var response = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 {statusCode} Test\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(response);
            await stream.FlushAsync();
        });
        var result = await WebHealthChecker.ProbeAsync(
            new Uri($"http://127.0.0.1:{endpoint.Port}/"),
            TimeSpan.FromSeconds(2));
        await responseTask;
        return result;
    }
    finally
    {
        listener.Stop();
    }
}

static int ReserveLoopbackPort()
{
    var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    try
    {
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
    finally
    {
        listener.Stop();
    }
}

static async Task ServeHttpStatusAfterDelayAsync(
    int port,
    int statusCode,
    TimeSpan startDelay,
    CancellationToken cancellationToken)
{
    await Task.Delay(startDelay, cancellationToken);
    var listener = new TcpListener(IPAddress.Loopback, port);
    listener.Start();
    try
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            using var client = await listener.AcceptTcpClientAsync(cancellationToken);
            await using var stream = client.GetStream();
            var request = new byte[4096];
            _ = await stream.ReadAsync(request, cancellationToken);
            var response = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 {statusCode} Test\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(response, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }
    }
    finally
    {
        listener.Stop();
    }
}

static void VerifyIconResource()
{
    using var stream = typeof(ProcessSupervisor).Assembly.GetManifestResourceStream(
        "DshLauncher.Assets.dsh-launcher.ico");
    if (stream is null)
    {
        throw new InvalidOperationException("The application icon was not embedded.");
    }

    Span<byte> header = stackalloc byte[6];
    stream.ReadExactly(header);
    var iconType = BitConverter.ToUInt16(header[2..4]);
    var imageCount = BitConverter.ToUInt16(header[4..6]);
    if (iconType != 1 || imageCount < 2)
    {
        throw new InvalidOperationException("The embedded application icon is not a multi-size ICO file.");
    }
}

static async Task VerifyStartRequestsAreCoalescedAsync()
{
    var startCompletion = new TaskCompletionSource<StartResult>(TaskCreationOptions.RunContinuationsAsynchronously);
    var startCalls = 0;
    var browserCalls = 0;
    Uri? openedUrl = null;
    var coordinator = new StartCoordinator(
        () =>
        {
            startCalls++;
            return startCompletion.Task;
        },
        launchUrl =>
        {
            browserCalls++;
            openedUrl = launchUrl;
        });

    var automaticStart = coordinator.Request(openBrowser: false);
    var trayOpen = coordinator.Request(openBrowser: true);
    if (!automaticStart.IsOwner || trayOpen.IsOwner ||
        !ReferenceEquals(automaticStart.Completion, trayOpen.Completion))
    {
        throw new InvalidOperationException("Concurrent start requests were not coalesced.");
    }

    var launchUrl = new Uri("http://127.0.0.1:3080/?token=test-token");
    startCompletion.SetResult(new StartResult(
        Ready: true,
        Exited: false,
        ExitCode: null,
        Message: "ready",
        LaunchUrl: launchUrl));
    await Task.WhenAll(automaticStart.Completion, trayOpen.Completion);
    if (startCalls != 1 || browserCalls != 1 || openedUrl != launchUrl)
    {
        throw new InvalidOperationException(
            $"Expected one start and one browser open with the launch URL, got {startCalls} starts and {browserCalls} opens.");
    }
}

static void VerifyDshWebLaunchUrlParsing()
{
    const string line =
        "dsh web: http://127.0.0.1:3080/?token=secret-token (LAN: http://192.168.1.10:3080/?token=secret-token)";
    var launchUrl = ProcessSupervisor.TryParseDshWebLaunchUrl(line);
    if (launchUrl?.AbsoluteUri != "http://127.0.0.1:3080/?token=secret-token")
    {
        throw new InvalidOperationException($"The dsh web launch URL was not parsed correctly: {launchUrl}");
    }

    var redacted = ProcessSupervisor.RedactLaunchTokens(line);
    if (redacted.Contains("secret-token", StringComparison.Ordinal) ||
        !redacted.Contains("?token=<redacted>", StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"The dsh web launch URL was not redacted correctly: {redacted}");
    }

    if (ProcessSupervisor.TryParseDshWebLaunchUrl("dsh web: opening the default browser") is not null ||
        ProcessSupervisor.TryParseDshWebLaunchUrl("dsh web: http://127.0.0.1:3080/") is not null)
    {
        throw new InvalidOperationException("A non-authenticated dsh web line was accepted as a launch URL.");
    }
}

static async Task VerifyAuthenticatedWebLaunchAsync()
{
    var testDirectory = Path.Combine(Path.GetTempPath(), $"dsh-launcher-auth-{Guid.NewGuid():N}");
    var previousUrl = Environment.GetEnvironmentVariable("DSH_WEB_URL");
    var previousTimeout = Environment.GetEnvironmentVariable("DSH_START_TIMEOUT_SECONDS");
    var previousLogDirectory = Environment.GetEnvironmentVariable("DSH_LOG_DIR");
    var port = ReserveLoopbackPort();
    using var listenerCancellation = new CancellationTokenSource();
    Directory.CreateDirectory(testDirectory);
    try
    {
        var listener = ServeHttpStatusAfterDelayAsync(
            port,
            statusCode: 401,
            startDelay: TimeSpan.FromMilliseconds(1500),
            listenerCancellation.Token);
        var fakeHarness = Path.Combine(testDirectory, "auth-dsh.cmd");
        File.WriteAllText(
            fakeHarness,
            $"@echo dsh web: http://127.0.0.1:{port}/?token=test-token\r\n" +
            "@ping -n 8 127.0.0.1 > nul\r\n");
        Environment.SetEnvironmentVariable("DSH_WEB_URL", $"http://127.0.0.1:{port}/");
        Environment.SetEnvironmentVariable("DSH_START_TIMEOUT_SECONDS", "5");
        Environment.SetEnvironmentVariable("DSH_LOG_DIR", Path.Combine(testDirectory, "logs"));

        var config = LauncherConfig.Load();
        using var logger = LauncherLogger.Create(config.LogDirectory);
        using var supervisor = new ProcessSupervisor(
            config,
            logger,
            _ => Task.FromResult(new RunnerSpec(
                fakeHarness,
                Array.Empty<string>(),
                testDirectory,
                "auth test runner")));

        var result = await supervisor.StartAsync();
        try
        {
            if (!result.Ready ||
                result.Exited ||
                result.LaunchUrl?.AbsoluteUri != $"http://127.0.0.1:{port}/?token=test-token" ||
                !result.Message.Contains("authentication is required", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Authenticated startup result was not ready: {result}");
            }

            var log = await ReadSharedTextAsync(logger.FilePath);
            if (log.Contains("test-token", StringComparison.Ordinal) ||
                !log.Contains("?token=<redacted>", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The authenticated launch token was not redacted in the log.");
            }
        }
        finally
        {
            await supervisor.StopAsync();
            await listenerCancellation.CancelAsync();
            try
            {
                await listener;
            }
            catch (OperationCanceledException)
            {
            }
        }
    }
    finally
    {
        Environment.SetEnvironmentVariable("DSH_WEB_URL", previousUrl);
        Environment.SetEnvironmentVariable("DSH_START_TIMEOUT_SECONDS", previousTimeout);
        Environment.SetEnvironmentVariable("DSH_LOG_DIR", previousLogDirectory);
        if (Directory.Exists(testDirectory))
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }
}

static async Task<string> ReadSharedTextAsync(string path)
{
    await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
    using var reader = new StreamReader(stream, Encoding.UTF8);
    return await reader.ReadToEndAsync();
}

static async Task VerifyStartupTimeoutAsync()
{
    var testDirectory = Path.Combine(Path.GetTempPath(), $"dsh-launcher-timeout-{Guid.NewGuid():N}");
    var previousUrl = Environment.GetEnvironmentVariable("DSH_WEB_URL");
    var previousTimeout = Environment.GetEnvironmentVariable("DSH_START_TIMEOUT_SECONDS");
    var previousLogDirectory = Environment.GetEnvironmentVariable("DSH_LOG_DIR");
    Directory.CreateDirectory(testDirectory);
    try
    {
        var fakeHarness = Path.Combine(testDirectory, "slow-dsh.cmd");
        File.WriteAllText(
            fakeHarness,
            "@echo fake Harness still starting\r\n@ping -n 6 127.0.0.1 > nul\r\n");
        Environment.SetEnvironmentVariable("DSH_WEB_URL", "http://127.0.0.1:1/");
        Environment.SetEnvironmentVariable("DSH_START_TIMEOUT_SECONDS", "1");
        Environment.SetEnvironmentVariable("DSH_LOG_DIR", Path.Combine(testDirectory, "logs"));

        var config = LauncherConfig.Load();
        using var logger = LauncherLogger.Create(config.LogDirectory);
        using var supervisor = new ProcessSupervisor(
            config,
            logger,
            _ => Task.FromResult(new RunnerSpec(
                fakeHarness,
                Array.Empty<string>(),
                testDirectory,
                "slow test runner")));

        var result = await supervisor.StartAsync();
        try
        {
            if (result.Ready || result.Exited ||
                !result.Message.Contains("did not respond", StringComparison.Ordinal) ||
                !result.Message.Contains("within 1 seconds", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Startup timeout result was not actionable: {result}");
            }
        }
        finally
        {
            await supervisor.StopAsync();
        }
    }
    finally
    {
        Environment.SetEnvironmentVariable("DSH_WEB_URL", previousUrl);
        Environment.SetEnvironmentVariable("DSH_START_TIMEOUT_SECONDS", previousTimeout);
        Environment.SetEnvironmentVariable("DSH_LOG_DIR", previousLogDirectory);
        if (Directory.Exists(testDirectory))
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }
}

static async Task VerifyNpxFallbackAsync()
{
    var testDirectory = Path.Combine(Path.GetTempPath(), $"dsh-launcher-tests-{Guid.NewGuid():N}");
    var previousPath = Environment.GetEnvironmentVariable("PATH");
    var previousHarnessDirectory = Environment.GetEnvironmentVariable("DEEPSEEK_HARNESS_DIR");
    var previousBinary = Environment.GetEnvironmentVariable("DEEPSEEK_DSH_BIN");
    var previousDshHome = Environment.GetEnvironmentVariable("DSH_HOME");
    var previousCurrentDirectory = Environment.CurrentDirectory;
    Directory.CreateDirectory(testDirectory);
    try
    {
        var shimDirectory = Path.Combine(testDirectory, "shim");
        var toolDirectory = Path.Combine(testDirectory, "tools");
        Directory.CreateDirectory(shimDirectory);
        Directory.CreateDirectory(toolDirectory);
        File.WriteAllText(Path.Combine(shimDirectory, "dsh.cmd"), "@dsh-launcher.exe");
        File.WriteAllText(Path.Combine(shimDirectory, "dsh-launcher.ps1"), string.Empty);
        var npx = Path.Combine(toolDirectory, "npx.cmd");
        File.WriteAllText(npx, "@exit /b 0");
        Environment.SetEnvironmentVariable("PATH", $"{shimDirectory}{Path.PathSeparator}{toolDirectory}");
        Environment.SetEnvironmentVariable("DEEPSEEK_HARNESS_DIR", null);
        Environment.SetEnvironmentVariable("DEEPSEEK_DSH_BIN", null);
        Environment.SetEnvironmentVariable("DSH_HOME", Path.Combine(testDirectory, "dsh-home"));
        Environment.CurrentDirectory = testDirectory;

        using var logger = LauncherLogger.Create(Path.Combine(testDirectory, "logs"));
        var resolved = await new RunnerResolver(logger).ResolveAsync();
        var expected = new[] { "--yes", "--package=@deepseek-ai/dsh", "--", "dsh" };
        if (!resolved.PrefixArguments.SequenceEqual(expected))
        {
            throw new InvalidOperationException(
                $"Expected forced npx package arguments '{string.Join(' ', expected)}', got '{string.Join(' ', resolved.PrefixArguments)}'.");
        }

        string? childPath = null;
        if (resolved.EnvironmentOverrides is null ||
            !resolved.EnvironmentOverrides.TryGetValue("PATH", out childPath) ||
            !string.Equals(childPath, toolDirectory, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Expected the launcher shim directory to be removed from child PATH, got '{childPath}'.");
        }

        var startInfo = ProcessLauncher.CreateStartInfo(resolved, redirectOutput: true, hiddenWindow: true);
        if (!string.Equals(startInfo.Environment["PATH"], toolDirectory, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("ProcessLauncher did not apply the sanitized child PATH.");
        }
    }
    finally
    {
        Environment.CurrentDirectory = previousCurrentDirectory;
        Environment.SetEnvironmentVariable("PATH", previousPath);
        Environment.SetEnvironmentVariable("DEEPSEEK_HARNESS_DIR", previousHarnessDirectory);
        Environment.SetEnvironmentVariable("DEEPSEEK_DSH_BIN", previousBinary);
        Environment.SetEnvironmentVariable("DSH_HOME", previousDshHome);
        Directory.Delete(testDirectory, recursive: true);
    }
}

static async Task VerifyLocalPackageVersionAsync()
{
    var testDirectory = Path.Combine(Path.GetTempPath(), $"dsh-launcher-package-tests-{Guid.NewGuid():N}");
    var previousPath = Environment.GetEnvironmentVariable("PATH");
    var previousHarnessDirectory = Environment.GetEnvironmentVariable("DEEPSEEK_HARNESS_DIR");
    var previousBinary = Environment.GetEnvironmentVariable("DEEPSEEK_DSH_BIN");
    var previousDshHome = Environment.GetEnvironmentVariable("DSH_HOME");
    var previousCurrentDirectory = Environment.CurrentDirectory;
    Directory.CreateDirectory(testDirectory);
    try
    {
        var toolDirectory = Path.Combine(testDirectory, "tools");
        var packageDirectory = Path.Combine(testDirectory, "node_modules", "@deepseek-ai", "dsh");
        Directory.CreateDirectory(toolDirectory);
        Directory.CreateDirectory(Path.Combine(packageDirectory, "lib"));
        File.WriteAllText(Path.Combine(toolDirectory, "node.cmd"), "@exit /b 0");
        File.WriteAllText(Path.Combine(packageDirectory, "lib", "bin.js"), string.Empty);
        File.WriteAllText(
            Path.Combine(packageDirectory, "package.json"),
            "{\"name\":\"@deepseek-ai/dsh\",\"version\":\"0.1.0-rc.8\"}");
        Environment.SetEnvironmentVariable("PATH", toolDirectory);
        Environment.SetEnvironmentVariable("DEEPSEEK_HARNESS_DIR", null);
        Environment.SetEnvironmentVariable("DEEPSEEK_DSH_BIN", null);
        Environment.SetEnvironmentVariable("DSH_HOME", Path.Combine(testDirectory, "dsh-home"));
        Environment.CurrentDirectory = testDirectory;

        using var logger = LauncherLogger.Create(Path.Combine(testDirectory, "logs"));
        var resolved = await new RunnerResolver(logger).ResolveAsync();
        if (resolved.DshVersion != "0.1.0-rc.8")
        {
            throw new InvalidOperationException(
                $"Expected local Harness version 0.1.0-rc.8, got {resolved.DshVersion ?? "unknown"}.");
        }

        var localWebRunner = ProcessSupervisor.AddWebCommand(resolved);
        if (!localWebRunner.PrefixArguments.TakeLast(2).SequenceEqual(new[] { "web", "--no-open" }))
        {
            throw new InvalidOperationException("Expected an rc.8 local package to disable Harness browser opening.");
        }
    }
    finally
    {
        Environment.CurrentDirectory = previousCurrentDirectory;
        Environment.SetEnvironmentVariable("PATH", previousPath);
        Environment.SetEnvironmentVariable("DEEPSEEK_HARNESS_DIR", previousHarnessDirectory);
        Environment.SetEnvironmentVariable("DEEPSEEK_DSH_BIN", previousBinary);
        Environment.SetEnvironmentVariable("DSH_HOME", previousDshHome);
        Directory.Delete(testDirectory, recursive: true);
    }
}

static async Task VerifyManagedHarnessResolutionAsync()
{
    var testDirectory = Path.Combine(Path.GetTempPath(), $"dsh-launcher-managed-tests-{Guid.NewGuid():N}");
    var managedRoot = Path.Combine(testDirectory, "managed harness");
    var previousPath = Environment.GetEnvironmentVariable("PATH");
    var previousHarnessDirectory = Environment.GetEnvironmentVariable("DEEPSEEK_HARNESS_DIR");
    var previousBinary = Environment.GetEnvironmentVariable("DEEPSEEK_DSH_BIN");
    var previousDshHome = Environment.GetEnvironmentVariable("DSH_HOME");
    var previousManagedRoot = Environment.GetEnvironmentVariable("DSH_LAUNCHER_MANAGED_ROOT");
    var previousCurrentDirectory = Environment.CurrentDirectory;
    Directory.CreateDirectory(testDirectory);
    try
    {
        var toolDirectory = Path.Combine(testDirectory, "tools");
        var packageDirectory = Path.Combine(managedRoot, "node_modules", "@deepseek-ai", "dsh");
        Directory.CreateDirectory(toolDirectory);
        Directory.CreateDirectory(Path.Combine(packageDirectory, "lib"));
        File.WriteAllText(Path.Combine(toolDirectory, "node.cmd"), "@exit /b 0");
        File.WriteAllText(Path.Combine(packageDirectory, "lib", "bin.js"), string.Empty);
        File.WriteAllText(Path.Combine(managedRoot, ".dsh-launcher-managed"), "dsh-launcher managed Harness v1");
        File.WriteAllText(
            Path.Combine(packageDirectory, "package.json"),
            "{\"name\":\"@deepseek-ai/dsh\",\"version\":\"0.1.1-rc.2\"}");
        Environment.SetEnvironmentVariable("PATH", toolDirectory);
        Environment.SetEnvironmentVariable("DEEPSEEK_HARNESS_DIR", null);
        Environment.SetEnvironmentVariable("DEEPSEEK_DSH_BIN", null);
        Environment.SetEnvironmentVariable("DSH_HOME", Path.Combine(testDirectory, "dsh-home"));
        Environment.SetEnvironmentVariable("DSH_LAUNCHER_MANAGED_ROOT", managedRoot);
        Environment.CurrentDirectory = testDirectory;

        using var logger = LauncherLogger.Create(Path.Combine(testDirectory, "logs"));
        var resolver = new RunnerResolver(logger);
        var installer = new ManagedHarnessInstaller(resolver, logger);
        var assessment = await installer.AssessAsync();
        if (assessment.ExistingRunner is null ||
            assessment.ExistingRunner.DshVersion != "0.1.1-rc.2" ||
            !assessment.ExistingRunner.Description.Contains("launcher-managed", StringComparison.OrdinalIgnoreCase) ||
            installer.ReadManagedVersion() != "0.1.1-rc.2")
        {
            throw new InvalidOperationException("The launcher-managed Harness package was not resolved correctly.");
        }

        await installer.RemoveAsync(progress: null);
        if (Directory.Exists(managedRoot))
        {
            throw new InvalidOperationException("The launcher-managed Harness directory was not removed.");
        }
    }
    finally
    {
        Environment.CurrentDirectory = previousCurrentDirectory;
        Environment.SetEnvironmentVariable("PATH", previousPath);
        Environment.SetEnvironmentVariable("DEEPSEEK_HARNESS_DIR", previousHarnessDirectory);
        Environment.SetEnvironmentVariable("DEEPSEEK_DSH_BIN", previousBinary);
        Environment.SetEnvironmentVariable("DSH_HOME", previousDshHome);
        Environment.SetEnvironmentVariable("DSH_LAUNCHER_MANAGED_ROOT", previousManagedRoot);
        if (Directory.Exists(testDirectory))
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }
}

static async Task VerifyManagedRemovalPreservesUnknownFilesAsync()
{
    var testDirectory = Path.Combine(Path.GetTempPath(), $"dsh-launcher-managed-remove-{Guid.NewGuid():N}");
    var managedRoot = Path.Combine(testDirectory, "managed harness");
    var previousManagedRoot = Environment.GetEnvironmentVariable("DSH_LAUNCHER_MANAGED_ROOT");
    Directory.CreateDirectory(Path.Combine(managedRoot, "node_modules", "generated"));
    Directory.CreateDirectory(Path.Combine(managedRoot, ".tools", "generated"));
    try
    {
        File.WriteAllText(Path.Combine(managedRoot, ".dsh-launcher-managed"), "dsh-launcher managed Harness v1");
        File.WriteAllText(Path.Combine(managedRoot, "package.json"), "{}");
        File.WriteAllText(Path.Combine(managedRoot, "pnpm-lock.yaml"), "lockfileVersion: '9.0'");
        File.WriteAllText(Path.Combine(managedRoot, "pnpm-workspace.yaml"), "allowBuilds: {}");
        File.WriteAllText(Path.Combine(managedRoot, "keep.txt"), "user file");
        Environment.SetEnvironmentVariable("DSH_LAUNCHER_MANAGED_ROOT", managedRoot);

        using var logger = LauncherLogger.Create(Path.Combine(testDirectory, "logs"));
        var installer = new ManagedHarnessInstaller(new RunnerResolver(logger), logger);
        await installer.RemoveAsync(progress: null);
        if (!Directory.Exists(managedRoot) || !File.Exists(Path.Combine(managedRoot, "keep.txt")) ||
            Directory.Exists(Path.Combine(managedRoot, "node_modules")) ||
            Directory.Exists(Path.Combine(managedRoot, ".tools")) ||
            File.Exists(Path.Combine(managedRoot, ".dsh-launcher-managed")))
        {
            throw new InvalidOperationException("Managed removal did not preserve an unknown user file safely.");
        }
    }
    finally
    {
        Environment.SetEnvironmentVariable("DSH_LAUNCHER_MANAGED_ROOT", previousManagedRoot);
        if (Directory.Exists(testDirectory))
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }
}

internal sealed class RecommendationHealthStubHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

    public RecommendationHealthStubHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        _handler = handler;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(_handler(request));
    }
}
