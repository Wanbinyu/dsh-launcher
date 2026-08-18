using DshLauncher;

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

await VerifyNpxFallbackAsync();
await VerifyStartRequestsAreCoalescedAsync();
VerifyIconResource();
Console.WriteLine("DshLauncher tests passed.");

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
    var coordinator = new StartCoordinator(
        () =>
        {
            startCalls++;
            return startCompletion.Task;
        },
        () => browserCalls++);

    var automaticStart = coordinator.Request(openBrowser: false);
    var trayOpen = coordinator.Request(openBrowser: true);
    if (!automaticStart.IsOwner || trayOpen.IsOwner ||
        !ReferenceEquals(automaticStart.Completion, trayOpen.Completion))
    {
        throw new InvalidOperationException("Concurrent start requests were not coalesced.");
    }

    startCompletion.SetResult(new StartResult(
        Ready: true,
        Exited: false,
        ExitCode: null,
        Message: "ready"));
    await Task.WhenAll(automaticStart.Completion, trayOpen.Completion);
    if (startCalls != 1 || browserCalls != 1)
    {
        throw new InvalidOperationException(
            $"Expected one start and one browser open, got {startCalls} starts and {browserCalls} opens.");
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
