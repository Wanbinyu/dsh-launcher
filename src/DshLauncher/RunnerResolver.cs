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
                $"local @deepseek-ai/dsh package at {localEntry}");
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
                $"global @deepseek-ai/dsh package at {globalEntry}");
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

        return new RunnerSpec(
            npx,
            new[] { "--yes", "@deepseek-ai/dsh" },
            Environment.CurrentDirectory,
            "the @deepseek-ai/dsh package through npx");
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
            $"source tree at {harnessDirectory}");
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
                $"configured JavaScript CLI at {binary}");
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

    private string? FindExistingDshCommand()
    {
        var ownDirectory = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar);
        foreach (var candidate in FindAllExecutables("dsh"))
        {
            var candidateDirectory = Path.GetDirectoryName(candidate)?.TrimEnd(Path.DirectorySeparatorChar);
            if (string.Equals(candidateDirectory, ownDirectory, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return candidate;
        }

        return null;
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

    private static string? FindExecutable(string name)
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
