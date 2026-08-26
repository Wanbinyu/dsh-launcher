using System.Diagnostics;
using System.Text.Json;

namespace DshLauncher;

internal sealed record HarnessEnvironmentAssessment(
    RunnerSpec? ExistingRunner,
    string? NodePath,
    Version? NodeVersion,
    string? NpmPath,
    string? NpxPath,
    string? WingetPath)
{
    public bool HasNodeAndNpm => NodePath is not null && NpmPath is not null;
    public bool HasCompatibleNodeAndNpm => HasNodeAndNpm && NodeVersion is not null &&
                                           NodeVersion >= ManagedHarnessInstaller.MinimumNodeVersion;
}

internal sealed record HarnessInstallProgress(string Stage, string Detail);

internal sealed class ManagedHarnessInstaller
{
    internal const string PackageName = "@deepseek-ai/dsh";
    internal const string NodePackageId = "OpenJS.NodeJS.LTS";
    internal const string PnpmVersion = "11.24.0";
    internal static readonly Version MinimumNodeVersion = new(22, 19, 0);
    internal static readonly string[] AllowedBuildDependencies =
    {
        "@deepseek-ai/dsh-subprocess-local",
        "@google/genai",
        "koffi",
        "node-pty",
        "protobufjs",
    };
    private readonly RunnerResolver _resolver;
    private readonly LauncherLogger _logger;
    private readonly SemaphoreSlim _operationLock = new(1, 1);

    public ManagedHarnessInstaller(RunnerResolver resolver, LauncherLogger logger)
    {
        _resolver = resolver;
        _logger = logger;
    }

    public async Task<HarnessEnvironmentAssessment> AssessAsync(
        CancellationToken cancellationToken = default)
    {
        var existing = await _resolver.TryResolveInstalledAsync(cancellationToken).ConfigureAwait(false);
        return await DetectEnvironmentAsync(existing, cancellationToken).ConfigureAwait(false);
    }

    public HarnessEnvironmentAssessment DetectEnvironment(RunnerSpec? existingRunner = null)
    {
        var node = RunnerResolver.FindNodeExecutable();
        return new HarnessEnvironmentAssessment(
            existingRunner,
            node,
            NodeVersion: null,
            FindNodeTool("npm", node),
            FindNodeTool("npx", node),
            RunnerResolver.FindExecutable("winget"));
    }

    public async Task<HarnessEnvironmentAssessment> InstallNodeLtsAsync(
        IProgress<HarnessInstallProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        var before = await DetectEnvironmentAsync(existingRunner: null, cancellationToken).ConfigureAwait(false);
        if (before.HasCompatibleNodeAndNpm)
        {
            return before;
        }

        if (before.WingetPath is null)
        {
            throw new InvalidOperationException(
                "Windows Package Manager (winget) was not found. Install Node.js LTS from https://nodejs.org/ and try again.");
        }

        progress?.Report(new HarnessInstallProgress(
            "正在安装 Node.js LTS / Installing Node.js LTS",
            "Windows 可能会显示权限确认窗口，请允许安装继续。"));
        var wingetRunner = new RunnerSpec(
            before.WingetPath,
            BuildWingetInstallArguments(),
            Environment.CurrentDirectory,
            "Node.js LTS through Windows Package Manager");
        var result = await RunStreamingAsync(wingetRunner, progress, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            throw CreateCommandFailure("Node.js LTS installation failed", result);
        }

        RefreshProcessPath();
        var after = await DetectEnvironmentAsync(existingRunner: null, cancellationToken).ConfigureAwait(false);
        if (!after.HasCompatibleNodeAndNpm)
        {
            throw new InvalidOperationException(
                $"Node.js installation completed, but Node.js >= {MinimumNodeVersion} and npm could not be verified. " +
                "Restart Windows or install Node.js LTS manually, then try again.");
        }

        progress?.Report(new HarnessInstallProgress(
            "Node.js 已就绪 / Node.js is ready",
            $"v{after.NodeVersion}"));
        return after;
    }

    public async Task<RunnerSpec> InstallOrRepairAsync(
        IProgress<HarnessInstallProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await InstallOrRepairCoreAsync(progress, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    private async Task<RunnerSpec> InstallOrRepairCoreAsync(
        IProgress<HarnessInstallProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        var environment = await DetectEnvironmentAsync(existingRunner: null, cancellationToken).ConfigureAwait(false);
        if (!environment.HasCompatibleNodeAndNpm)
        {
            environment = await InstallNodeLtsAsync(progress, cancellationToken).ConfigureAwait(false);
        }

        progress?.Report(new HarnessInstallProgress(
            "正在查询官方 npm 包 / Checking the official npm package",
            $"包名：{PackageName}"));
        var version = await FindPublishedVersionAsync(environment, progress, cancellationToken).ConfigureAwait(false);
        var root = Path.GetFullPath(ManagedHarnessPaths.GetRoot());
        var entry = ManagedHarnessPaths.GetPackageEntry();
        var wasInstalled = File.Exists(entry);
        ValidateManagedRoot(root);
        if (Directory.Exists(root) &&
            !File.Exists(ManagedHarnessPaths.GetMarker()) &&
            Directory.EnumerateFileSystemEntries(root).Any())
        {
            throw new InvalidOperationException(
                $"The managed Harness directory is not empty and has no dsh-launcher ownership marker: {root}");
        }
        Directory.CreateDirectory(root);
        EnsureManagedPackageManifest(root);
        var pnpmEntry = await EnsureManagedPnpmAsync(environment, progress, cancellationToken).ConfigureAwait(false);

        progress?.Report(new HarnessInstallProgress(
            "正在安装 DeepSeek Harness / Installing DeepSeek Harness",
            $"{PackageName}@{version}\n安装位置：{root}\n首次安装需要解析较多官方子包，请耐心等待。"));
        var installRunner = new RunnerSpec(
            environment.NodePath!,
            BuildPnpmInstallArguments(pnpmEntry, version),
            root,
            $"pnpm installation of {PackageName}@{version}",
            BuildNodeEnvironment(environment.NodePath!));
        var result = await RunStreamingAsync(installRunner, progress, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            throw CreateCommandFailure("DeepSeek Harness installation failed", result);
        }

        if (wasInstalled)
        {
            progress?.Report(new HarnessInstallProgress(
                "正在修复本机依赖 / Repairing native dependencies",
                "重新执行允许的官方依赖构建脚本。"));
            var repairRunner = new RunnerSpec(
                environment.NodePath!,
                BuildPnpmRebuildArguments(pnpmEntry),
                root,
                $"pnpm rebuild of {PackageName}@{version}",
                BuildNodeEnvironment(environment.NodePath!));
            var repairResult = await RunStreamingAsync(repairRunner, progress, cancellationToken).ConfigureAwait(false);
            if (repairResult.ExitCode != 0)
            {
                throw CreateCommandFailure("DeepSeek Harness dependency repair failed", repairResult);
            }
        }

        var installedVersion = ReadManagedVersion();
        if (!File.Exists(entry) || !string.Equals(installedVersion, version, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The package manager finished, but {PackageName} could not be validated at {root}.");
        }

        progress?.Report(new HarnessInstallProgress(
            "安装完成 / Installation complete",
            $"DeepSeek Harness {installedVersion} 已准备就绪。"));
        _logger.Info($"Installed launcher-managed {PackageName} {installedVersion} at {root}.");
        return new RunnerSpec(
            environment.NodePath!,
            new[] { entry },
            Environment.CurrentDirectory,
            $"launcher-managed {PackageName} {installedVersion} at {entry}",
            DshVersion: installedVersion);
    }

    public async Task RemoveAsync(
        IProgress<HarnessInstallProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await RemoveCoreAsync(progress, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    private async Task RemoveCoreAsync(
        IProgress<HarnessInstallProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(ManagedHarnessPaths.GetRoot());
        ValidateManagedRoot(root);
        if (!Directory.Exists(root))
        {
            return;
        }
        if (!File.Exists(ManagedHarnessPaths.GetMarker()))
        {
            throw new InvalidOperationException(
                $"Refusing to remove {root} because the dsh-launcher ownership marker is missing.");
        }

        progress?.Report(new HarnessInstallProgress(
            "正在移除受管 Harness / Removing managed Harness",
            root));
        await Task.Run(() => RemoveOwnedFiles(root, cancellationToken), cancellationToken).ConfigureAwait(false);
        _logger.Info($"Removed launcher-managed Harness files at {root}; unknown files, if any, were preserved.");
    }

    public bool HasManagedInstallation()
    {
        return Directory.Exists(ManagedHarnessPaths.GetRoot()) && File.Exists(ManagedHarnessPaths.GetMarker());
    }

    public string? ReadManagedVersion()
    {
        var packageJson = ManagedHarnessPaths.GetPackageJson();
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(packageJson));
            var root = document.RootElement;
            if (!root.TryGetProperty("name", out var name) ||
                !string.Equals(name.GetString(), PackageName, StringComparison.Ordinal) ||
                !root.TryGetProperty("version", out var version) ||
                version.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            return version.GetString();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    internal static IReadOnlyList<string> BuildWingetInstallArguments()
    {
        return new[]
        {
            "install",
            "--id", NodePackageId,
            "--exact",
            "--source", "winget",
            "--accept-package-agreements",
            "--accept-source-agreements",
            "--silent",
            "--disable-interactivity",
        };
    }

    internal static Version? ParseNodeVersion(string output)
    {
        var value = output.Trim().TrimStart('v');
        return Version.TryParse(value, out var version) ? version : null;
    }

    internal static IReadOnlyList<string> BuildPnpmBootstrapArguments(string toolsRoot)
    {
        return new[]
        {
            "install",
            "--prefix", Path.GetFullPath(toolsRoot),
            "--save-exact",
            "--no-audit",
            "--no-fund",
            $"pnpm@{PnpmVersion}",
        };
    }

    internal static IReadOnlyList<string> BuildPnpmInstallArguments(string pnpmEntry, string version)
    {
        return new[]
        {
            Path.GetFullPath(pnpmEntry),
            "add",
            "--save-exact",
            "--reporter=append-only",
            $"{PackageName}@{version}",
        };
    }

    internal static IReadOnlyList<string> BuildPnpmRebuildArguments(string pnpmEntry)
    {
        return new[]
        {
            Path.GetFullPath(pnpmEntry),
            "rebuild",
            "--reporter=append-only",
        };
    }

    private async Task<string> EnsureManagedPnpmAsync(
        HarnessEnvironmentAssessment environment,
        IProgress<HarnessInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        var pnpmEntry = ManagedHarnessPaths.GetPnpmEntry();
        if (IsManagedPnpmValid())
        {
            return pnpmEntry;
        }

        var toolsRoot = ManagedHarnessPaths.GetToolsRoot();
        Directory.CreateDirectory(toolsRoot);
        progress?.Report(new HarnessInstallProgress(
            "正在准备轻量包管理器 / Preparing the package manager",
            $"pnpm {PnpmVersion} 将安装到启动器受管目录，用于降低 Harness 安装耗时和内存占用。"));
        var runner = new RunnerSpec(
            environment.NpmPath!,
            BuildPnpmBootstrapArguments(toolsRoot),
            toolsRoot,
            $"managed pnpm {PnpmVersion} bootstrap",
            BuildNodeEnvironment(environment.NodePath!));
        var result = await RunStreamingAsync(runner, progress, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            throw CreateCommandFailure("Could not prepare the managed pnpm runtime", result);
        }

        if (!IsManagedPnpmValid())
        {
            throw new InvalidOperationException(
                $"pnpm installation completed, but pnpm {PnpmVersion} could not be validated at {pnpmEntry}.");
        }

        return pnpmEntry;
    }

    private static void EnsureManagedPackageManifest(string root)
    {
        var packageJson = Path.Combine(root, "package.json");
        var manifest = new Dictionary<string, object?>
        {
            ["name"] = "dsh-launcher-managed-harness",
            ["private"] = true,
            ["version"] = "0.0.0",
        };
        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });
        File.WriteAllText(packageJson, json + Environment.NewLine);
        File.WriteAllText(
            Path.Combine(root, "pnpm-workspace.yaml"),
            BuildPnpmWorkspaceConfig());
        File.WriteAllText(
            ManagedHarnessPaths.GetMarker(),
            "dsh-launcher managed Harness v1" + Environment.NewLine);
    }

    private static bool IsManagedPnpmValid()
    {
        if (!File.Exists(ManagedHarnessPaths.GetPnpmEntry()))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(ManagedHarnessPaths.GetPnpmPackageJson()));
            var root = document.RootElement;
            return root.TryGetProperty("name", out var name) && name.GetString() == "pnpm" &&
                   root.TryGetProperty("version", out var version) && version.GetString() == PnpmVersion;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }

    internal static string BuildPnpmWorkspaceConfig()
    {
        return "allowBuilds:" + Environment.NewLine +
               string.Join(
                   Environment.NewLine,
                   AllowedBuildDependencies.Select(package => $"  '{package}': true")) +
               Environment.NewLine;
    }

    private async Task<string> FindPublishedVersionAsync(
        HarnessEnvironmentAssessment environment,
        IProgress<HarnessInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        var runner = new RunnerSpec(
            environment.NpmPath!,
            new[] { "view", PackageName, "version", "--json" },
            Environment.CurrentDirectory,
            $"published {PackageName} version lookup",
            BuildNodeEnvironment(environment.NodePath!));
        var result = await RunStreamingAsync(runner, progress, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            throw CreateCommandFailure($"Could not query {PackageName} from npm", result);
        }

        var value = result.StandardOutput.Trim();
        try
        {
            using var document = JsonDocument.Parse(value);
            var version = document.RootElement.ValueKind == JsonValueKind.String
                ? document.RootElement.GetString()
                : null;
            if (!string.IsNullOrWhiteSpace(version))
            {
                return version;
            }
        }
        catch (JsonException)
        {
        }

        throw new InvalidOperationException($"npm returned an invalid {PackageName} version: {value}");
    }

    private async Task<HarnessEnvironmentAssessment> DetectEnvironmentAsync(
        RunnerSpec? existingRunner,
        CancellationToken cancellationToken)
    {
        var environment = DetectEnvironment(existingRunner);
        if (environment.NodePath is null)
        {
            return environment;
        }

        var version = await ReadNodeVersionAsync(environment.NodePath, cancellationToken).ConfigureAwait(false);
        return environment with { NodeVersion = version };
    }

    private async Task<InstallerCommandResult> RunStreamingAsync(
        RunnerSpec runner,
        IProgress<HarnessInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        _logger.Info($"Running {runner.Description}.");
        using var process = ProcessLauncher.Start(runner, redirectOutput: true, hiddenWindow: true);
        using var cancellationRegistration = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
            }
        });

        var standardOutput = new List<string>();
        var recentOutput = new Queue<string>();
        var outputTask = PumpInstallerOutputAsync(
            process.StandardOutput,
            "SETUP-OUT",
            standardOutput,
            recentOutput,
            progress,
            cancellationToken);
        var errorTask = PumpInstallerOutputAsync(
            process.StandardError,
            "SETUP-ERR",
            null,
            recentOutput,
            progress,
            cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        await Task.WhenAll(outputTask, errorTask).ConfigureAwait(false);
        return new InstallerCommandResult(
            process.ExitCode,
            string.Join(Environment.NewLine, standardOutput),
            recentOutput.ToArray());
    }

    private async Task PumpInstallerOutputAsync(
        StreamReader reader,
        string streamName,
        List<string>? standardOutput,
        Queue<string> recentOutput,
        IProgress<HarnessInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            _logger.WriteProcessOutput(streamName, line);
            lock (recentOutput)
            {
                standardOutput?.Add(line);
                recentOutput.Enqueue(line);
                while (recentOutput.Count > 12)
                {
                    recentOutput.Dequeue();
                }
            }

            if (!string.IsNullOrWhiteSpace(line))
            {
                progress?.Report(new HarnessInstallProgress(
                    "正在配置环境 / Configuring the environment",
                    line.Length <= 240 ? line : line[..240]));
            }
        }
    }

    private static IReadOnlyDictionary<string, string?> BuildNodeEnvironment(string nodePath)
    {
        var nodeDirectory = Path.GetDirectoryName(nodePath)!;
        var currentPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var pathEntries = currentPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        var path = pathEntries.Any(entry =>
            string.Equals(entry.Trim().Trim('"'), nodeDirectory, StringComparison.OrdinalIgnoreCase))
            ? currentPath
            : nodeDirectory + Path.PathSeparator + currentPath;
        return new Dictionary<string, string?>
        {
            ["PATH"] = path,
            ["npm_config_update_notifier"] = "false",
        };
    }

    private static string? FindNodeTool(string name, string? nodePath)
    {
        var fromPath = RunnerResolver.FindExecutable(name);
        if (fromPath is not null)
        {
            return fromPath;
        }

        if (nodePath is null)
        {
            return null;
        }

        var directory = Path.GetDirectoryName(nodePath);
        if (directory is null)
        {
            return null;
        }

        foreach (var extension in new[] { ".cmd", ".exe", string.Empty })
        {
            var candidate = Path.Combine(directory, name + extension);
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return null;
    }

    private static void RefreshProcessPath()
    {
        var machine = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine);
        var user = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User);
        var current = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Process);
        var combined = string.Join(
            Path.PathSeparator,
            new[] { machine, user, current }.Where(value => !string.IsNullOrWhiteSpace(value)));
        Environment.SetEnvironmentVariable("PATH", combined, EnvironmentVariableTarget.Process);
    }

    private static async Task<Version?> ReadNodeVersionAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var result = await ProcessLauncher.CaptureAsync(
            new RunnerSpec(path, new[] { "--version" }, Environment.CurrentDirectory, $"{path} version"),
            cancellationToken).ConfigureAwait(false);
        return result.ExitCode == 0 ? ParseNodeVersion(result.StandardOutput) : null;
    }

    private static InvalidOperationException CreateCommandFailure(
        string message,
        InstallerCommandResult result)
    {
        var details = result.RecentOutput.Length == 0
            ? "No command output was captured."
            : string.Join(Environment.NewLine, result.RecentOutput);
        return new InvalidOperationException($"{message} (exit code {result.ExitCode}).\n\n{details}");
    }

    private static void ValidateManagedRoot(string root)
    {
        var driveRoot = Path.GetPathRoot(root);
        if (string.IsNullOrWhiteSpace(driveRoot) ||
            string.Equals(root.TrimEnd(Path.DirectorySeparatorChar), driveRoot.TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Refusing to remove an unsafe managed Harness path.");
        }

        var configured = Environment.GetEnvironmentVariable("DSH_LAUNCHER_MANAGED_ROOT");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return;
        }

        var expectedParent = Path.GetFullPath(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "dsh-launcher")) + Path.DirectorySeparatorChar;
        if (!root.StartsWith(expectedParent, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Refusing to remove a managed Harness path outside dsh-launcher data.");
        }
    }

    private static void RemoveOwnedFiles(string root, CancellationToken cancellationToken)
    {
        foreach (var directory in new[]
        {
            Path.Combine(root, "node_modules"),
            Path.Combine(root, ".tools"),
        })
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        foreach (var file in new[]
        {
            Path.Combine(root, "package.json"),
            Path.Combine(root, "pnpm-lock.yaml"),
            Path.Combine(root, "pnpm-workspace.yaml"),
            Path.Combine(root, ".dsh-launcher-managed"),
        })
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }

        if (!Directory.EnumerateFileSystemEntries(root).Any())
        {
            Directory.Delete(root, recursive: false);
        }
    }

    private sealed record InstallerCommandResult(
        int ExitCode,
        string StandardOutput,
        string[] RecentOutput);
}
