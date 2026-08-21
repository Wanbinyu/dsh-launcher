using System.Text.Json;

namespace DshLauncher;

internal sealed class RunnerResolver
{
    private readonly LauncherLogger _logger;

    public RunnerResolver(LauncherLogger logger)
    {
        _logger = logger;
    }

    public async Task<RunnerSpec> ResolveAsync(CancellationToken cancellationToken = default)
    {
        var sourceRunner = GetRunnerFromSource();
        if (sourceRunner is not null)
        {
            return sourceRunner;
        }

        var configuredRunner = GetRunnerFromConfiguredBinary();
        if (configuredRunner is not null)
        {
            return configuredRunner;
        }

        var localEntry = FindLocalPackageEntry();
        if (localEntry is not null)
        {
            var node = FindExecutable("node");
            if (node is null)
            {
                throw new InvalidOperationException("A local @deepseek-ai/dsh package was found, but node was not found on PATH.");
            }

            return new RunnerSpec(
                node,
                new[] { localEntry },
                Environment.CurrentDirectory,
                $"local @deepseek-ai/dsh package at {localEntry}",
                DshVersion: FindDshPackageVersion(localEntry));
        }

        var installedEntry = FindInstalledPackageEntry();
        if (installedEntry is not null)
        {
            var node = FindExecutable("node");
            if (node is null)
            {
                throw new InvalidOperationException("An installed @deepseek-ai/dsh package was found, but node was not found on PATH.");
            }

            return new RunnerSpec(
                node,
                new[] { installedEntry },
                Environment.CurrentDirectory,
                $"installed @deepseek-ai/dsh package at {installedEntry}",
                DshVersion: FindDshPackageVersion(installedEntry));
        }

        var globalEntry = await FindGlobalPackageEntryAsync(cancellationToken).ConfigureAwait(false);
        if (globalEntry is not null)
        {
            var node = FindExecutable("node");
            if (node is null)
            {
                throw new InvalidOperationException("A global @deepseek-ai/dsh package was found, but node was not found on PATH.");
            }

            return new RunnerSpec(
                node,
                new[] { globalEntry },
                Environment.CurrentDirectory,
                $"global @deepseek-ai/dsh package at {globalEntry}",
                DshVersion: FindDshPackageVersion(globalEntry));
        }

        var existingCommand = FindExistingDshCommand();
        if (existingCommand is not null)
        {
            return new RunnerSpec(
                existingCommand,
                Array.Empty<string>(),
                Environment.CurrentDirectory,
                $"existing dsh command at {existingCommand}");
        }

        var npx = FindExecutable("npx");
        if (npx is null)
        {
            throw new InvalidOperationException("Could not find a DeepSeek Harness source tree, an installed dsh CLI, or npx.");
        }

        var publishedVersion = await FindPublishedPackageVersionAsync(cancellationToken).ConfigureAwait(false);
        return new RunnerSpec(
            npx,
            new[] { "--yes", "--package=@deepseek-ai/dsh", "--", "dsh" },
            Environment.CurrentDirectory,
            "the @deepseek-ai/dsh package through npx",
            new Dictionary<string, string?>
            {
                ["PATH"] = GetPathWithoutLauncherShims()
            },
            publishedVersion);
    }

    private RunnerSpec? GetRunnerFromSource()
    {
        var configuredDirectory = Environment.GetEnvironmentVariable("DEEPSEEK_HARNESS_DIR");
        if (string.IsNullOrWhiteSpace(configuredDirectory))
        {
            return null;
        }

        var harnessDirectory = GetExistingDirectory(configuredDirectory, "DEEPSEEK_HARNESS_DIR");
        var packageJson = Path.Combine(harnessDirectory, "package.json");
        if (!File.Exists(packageJson))
        {
            throw new InvalidOperationException(
                $"DEEPSEEK_HARNESS_DIR is not a DeepSeek Harness source directory (package.json not found): {harnessDirectory}");
        }

        var pnpm = FindExecutable("pnpm");
        if (pnpm is null)
        {
            throw new InvalidOperationException("DEEPSEEK_HARNESS_DIR is set, but pnpm was not found on PATH.");
        }

        return new RunnerSpec(
            pnpm,
            new[] { "dsh" },
            harnessDirectory,
            $"source tree at {harnessDirectory}",
            DshVersion: ReadPackageVersion(packageJson));
    }

    private RunnerSpec? GetRunnerFromConfiguredBinary()
    {
        var configuredBinary = Environment.GetEnvironmentVariable("DEEPSEEK_DSH_BIN");
        if (string.IsNullOrWhiteSpace(configuredBinary))
        {
            return null;
        }

        var binary = GetExistingFile(configuredBinary, "DEEPSEEK_DSH_BIN");
        if (Path.GetExtension(binary).Equals(".js", StringComparison.OrdinalIgnoreCase))
        {
            var node = FindExecutable("node");
            if (node is null)
            {
                throw new InvalidOperationException("DEEPSEEK_DSH_BIN points to a JavaScript file, but node was not found on PATH.");
            }

            return new RunnerSpec(
                node,
                new[] { binary },
                Environment.CurrentDirectory,
                $"configured JavaScript CLI at {binary}",
                DshVersion: FindDshPackageVersion(binary));
        }

        return new RunnerSpec(
            binary,
            Array.Empty<string>(),
            Environment.CurrentDirectory,
            $"configured CLI at {binary}");
    }

    private string? FindLocalPackageEntry()
    {
        var directory = Path.GetFullPath(Environment.CurrentDirectory);
        while (true)
        {
            var entry = Path.Combine(directory, "node_modules", "@deepseek-ai", "dsh", "lib", "bin.js");
            if (File.Exists(entry))
            {
                return Path.GetFullPath(entry);
            }

            var parent = Directory.GetParent(directory)?.FullName;
            if (string.IsNullOrWhiteSpace(parent) ||
                string.Equals(parent, directory, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            directory = parent;
        }
    }

    private async Task<string?> FindGlobalPackageEntryAsync(CancellationToken cancellationToken)
    {
        var npm = FindExecutable("npm");
        if (npm is null)
        {
            return null;
        }

        var npmRunner = new RunnerSpec(
            npm,
            new[] { "root", "--global" },
            Environment.CurrentDirectory,
            "npm global root lookup");
        ProcessResult result;
        try
        {
            result = await ProcessLauncher.CaptureAsync(npmRunner, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _logger.Error("Could not inspect the global npm root", exception);
            return null;
        }

        if (result.ExitCode != 0)
        {
            return null;
        }

        var root = result.StandardOutput
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault()
            ?.Trim();
        if (string.IsNullOrWhiteSpace(root))
        {
            return null;
        }

        var entry = Path.Combine(root, "@deepseek-ai", "dsh", "lib", "bin.js");
        return File.Exists(entry) ? Path.GetFullPath(entry) : null;
    }

    private async Task<string?> FindPublishedPackageVersionAsync(CancellationToken cancellationToken)
    {
        var npm = FindExecutable("npm");
        if (npm is null)
        {
            return null;
        }

        var runner = new RunnerSpec(
            npm,
            new[] { "view", "@deepseek-ai/dsh", "version", "--json" },
            Environment.CurrentDirectory,
            "published @deepseek-ai/dsh version lookup");
        try
        {
            var result = await ProcessLauncher.CaptureAsync(runner, cancellationToken).ConfigureAwait(false);
            return result.ExitCode == 0 ? ParseVersionOutput(result.StandardOutput) : null;
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _logger.Error("Could not inspect the published dsh version", exception);
            return null;
        }
    }

    private static string? ParseVersionOutput(string output)
    {
        var value = output.Trim();
        if (value.Length == 0)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(value);
            return document.RootElement.ValueKind == JsonValueKind.String
                ? document.RootElement.GetString()
                : null;
        }
        catch (JsonException)
        {
            return value.Trim('"');
        }
    }

    private static string? FindDshPackageVersion(string entryPath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(entryPath));
        while (!string.IsNullOrWhiteSpace(directory))
        {
            var packageJson = Path.Combine(directory, "package.json");
            if (File.Exists(packageJson))
            {
                var version = ReadPackageVersion(packageJson, requireDshPackage: true);
                if (version is not null)
                {
                    return version;
                }
            }

            var parent = Directory.GetParent(directory)?.FullName;
            if (string.IsNullOrWhiteSpace(parent) ||
                string.Equals(parent, directory, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            directory = parent;
        }

        return null;
    }

    private static string? ReadPackageVersion(string packageJson, bool requireDshPackage = false)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(packageJson));
            var root = document.RootElement;
            if (requireDshPackage &&
                (!root.TryGetProperty("name", out var name) ||
                 !string.Equals(name.GetString(), "@deepseek-ai/dsh", StringComparison.Ordinal)))
            {
                return null;
            }

            return root.TryGetProperty("version", out var version) && version.ValueKind == JsonValueKind.String
                ? version.GetString()
                : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private string? FindExistingDshCommand()
    {
        var ownDirectory = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar);
        foreach (var candidate in FindAllExecutables("dsh"))
        {
            if (IsLauncherShim(candidate, ownDirectory))
            {
                _logger.Info($"Skipping dsh-launcher wrapper at {candidate}.");
                continue;
            }

            return candidate;
        }

        return null;
    }

    private static string? FindInstalledPackageEntry()
    {
        var configuredHome = Environment.GetEnvironmentVariable("DSH_HOME");
        var dshHome = string.IsNullOrWhiteSpace(configuredHome)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dsh")
            : Path.GetFullPath(configuredHome.Trim());
        var candidates = new[]
        {
            Path.Combine(dshHome, "profiles", "web", "node_modules", "@deepseek-ai", "dsh", "lib", "bin.js"),
            Path.Combine(dshHome, "profiles", "node_modules", "@deepseek-ai", "dsh", "lib", "bin.js"),
            Path.Combine(dshHome, "node_modules", "@deepseek-ai", "dsh", "lib", "bin.js")
        };

        return candidates.FirstOrDefault(File.Exists) is { } entry ? Path.GetFullPath(entry) : null;
    }

    private static bool IsLauncherShim(string candidate, string ownDirectory)
    {
        var fullPath = Path.GetFullPath(candidate);
        var candidateDirectory = Path.GetDirectoryName(fullPath)?.TrimEnd(Path.DirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(candidateDirectory))
        {
            return false;
        }

        if (string.Equals(candidateDirectory, ownDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var launcherMarkers = new[]
        {
            "dsh-launcher.exe",
            "dsh-launcher.dll",
            "dsh-launcher.ps1"
        };
        if (launcherMarkers.Any(marker => File.Exists(Path.Combine(candidateDirectory, marker))))
        {
            return true;
        }

        if (!ProcessLauncher.IsBatchFile(fullPath))
        {
            return false;
        }

        try
        {
            using var reader = new StreamReader(fullPath);
            var buffer = new char[64 * 1024];
            var length = reader.ReadBlock(buffer, 0, buffer.Length);
            return new string(buffer, 0, length).Contains("dsh-launcher", StringComparison.OrdinalIgnoreCase);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string GetPathWithoutLauncherShims()
    {
        var ownDirectory = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar);
        var launcherDirectories = FindAllExecutables("dsh")
            .Where(candidate => IsLauncherShim(candidate, ownDirectory))
            .Select(Path.GetDirectoryName)
            .Where(directory => !string.IsNullOrWhiteSpace(directory))
            .Select(directory => Path.GetFullPath(directory!).TrimEnd(Path.DirectorySeparatorChar))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var safeEntries = path
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(entry =>
            {
                try
                {
                    var directory = Path.GetFullPath(entry.Trim().Trim('"')).TrimEnd(Path.DirectorySeparatorChar);
                    return !launcherDirectories.Contains(directory);
                }
                catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
                {
                    return true;
                }
            });

        return string.Join(Path.PathSeparator, safeEntries);
    }

    private static string GetExistingDirectory(string value, string variableName)
    {
        var path = Path.GetFullPath(value.Trim());
        if (!Directory.Exists(path))
        {
            throw new InvalidOperationException($"{variableName} does not exist: {value}");
        }

        return path;
    }

    private static string GetExistingFile(string value, string variableName)
    {
        var path = Path.GetFullPath(value.Trim());
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"{variableName} does not exist: {value}");
        }

        return path;
    }

    internal static string? FindExecutable(string name)
    {
        return FindAllExecutables(name).FirstOrDefault();
    }

    private static IEnumerable<string> FindAllExecutables(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var pathExtensions = Environment.GetEnvironmentVariable("PATHEXT")?.Split(';') ??
            new[] { ".COM", ".EXE", ".BAT", ".CMD" };
        var names = Path.HasExtension(name)
            ? new[] { name }
            : pathExtensions.Select(extension => name + extension).Concat(new[] { name });

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var candidateName in names)
            {
                var candidate = Path.Combine(directory.Trim().Trim('"'), candidateName);
                if (File.Exists(candidate))
                {
                    yield return Path.GetFullPath(candidate);
                }
            }
        }
    }
}
