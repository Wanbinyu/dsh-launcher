using System.Net.NetworkInformation;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DshLauncher;

internal enum DoctorStatus
{
    Pass,
    Warning,
    Failure
}

internal sealed record DoctorCheck(string Id, DoctorStatus Status, string Message);

internal sealed record DoctorReport(
    DateTimeOffset GeneratedAt,
    string LauncherVersion,
    string WebUrl,
    string DshHome,
    IReadOnlyList<DoctorCheck> Checks)
{
    public bool HasFailures => Checks.Any(check => check.Status == DoctorStatus.Failure);

    public string ToText()
    {
        var builder = new StringBuilder();
        builder.AppendLine("dsh-launcher diagnostics");
        builder.AppendLine($"Generated: {GeneratedAt:O}");
        builder.AppendLine($"Launcher: {LauncherVersion}");
        builder.AppendLine($"Web URL: {WebUrl}");
        builder.AppendLine($"DSH home: {DshHome}");
        builder.AppendLine();
        foreach (var check in Checks)
        {
            var marker = check.Status switch
            {
                DoctorStatus.Pass => "PASS",
                DoctorStatus.Warning => "WARN",
                _ => "FAIL"
            };
            builder.AppendLine($"[{marker}] {check.Id}: {check.Message}");
        }

        builder.AppendLine();
        builder.Append(HasFailures
            ? "Result: problems found; review FAIL items before restarting DSH."
            : "Result: no blocking problem found.");
        return builder.ToString();
    }

    public string ToJson()
    {
        return JsonSerializer.Serialize(new
        {
            generatedAt = GeneratedAt,
            launcherVersion = LauncherVersion,
            webUrl = WebUrl,
            dshHome = DshHome,
            result = HasFailures ? "failed" : "ok",
            checks = Checks.Select(check => new
            {
                id = check.Id,
                status = check.Status.ToString().ToLowerInvariant(),
                message = check.Message
            })
        }, new JsonSerializerOptions { WriteIndented = true });
    }
}

internal sealed class DoctorRunner
{
    private static readonly Version MinimumNodeVersion = ManagedHarnessInstaller.MinimumNodeVersion;
    private static readonly string[] CoreRuntimePackages =
    {
        "cordis",
        "dsh-tools",
        "dsh-session",
        "dsh-llm"
    };
    private static readonly Regex SecretAssignment = new(
        @"(?i)\b(api[_-]?key|token|secret|password|authorization)\b(\s*[:=]\s*)([^\s,;]+)",
        RegexOptions.Compiled);
    private static readonly Regex UriCredentials = new(
        @"(?i)(https?://)([^/@\s]+)@",
        RegexOptions.Compiled);

    private readonly LauncherConfig _config;
    private readonly LauncherLogger _logger;
    private readonly List<DoctorCheck> _checks = new();

    public DoctorRunner(LauncherConfig config, LauncherLogger logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task<DoctorReport> RunAsync(CancellationToken cancellationToken = default)
    {
        var dshHome = GetDshHome();
        await CheckCliAsync(cancellationToken).ConfigureAwait(false);
        await CheckNodeAsync(cancellationToken).ConfigureAwait(false);
        CheckPackageManagers();
        CheckManagedHarness();
        CheckProfile(dshHome);
        CheckBundlesAndRuntimeCopies(dshHome);
        await CheckWebEndpointAsync(cancellationToken).ConfigureAwait(false);
        CheckLogDirectory();

        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "unknown";
        return new DoctorReport(
            DateTimeOffset.Now,
            version,
            SanitizeUri(_config.WebUrl),
            Redact(dshHome),
            _checks);
    }

    private async Task CheckCliAsync(CancellationToken cancellationToken)
    {
        try
        {
            var runner = await new RunnerResolver(_logger).ResolveAsync(cancellationToken).ConfigureAwait(false);
            Add("harness-cli", DoctorStatus.Pass, runner.Description);
        }
        catch (Exception exception)
        {
            Add("harness-cli", DoctorStatus.Failure, exception.Message);
        }
    }

    private async Task CheckNodeAsync(CancellationToken cancellationToken)
    {
        var node = RunnerResolver.FindNodeExecutable();
        if (node is null)
        {
            Add("node", DoctorStatus.Failure, "node was not found on PATH; DSH requires Node.js >= 22.19.0");
            return;
        }

        try
        {
            var result = await ProcessLauncher.CaptureAsync(new RunnerSpec(
                node,
                new[] { "--version" },
                Environment.CurrentDirectory,
                "Node.js version probe"), cancellationToken).ConfigureAwait(false);
            var raw = result.StandardOutput.Trim().TrimStart('v');
            if (result.ExitCode != 0 || !Version.TryParse(raw, out var version))
            {
                Add("node", DoctorStatus.Failure, $"could not read a valid version from {node}");
                return;
            }

            Add("node", version >= MinimumNodeVersion ? DoctorStatus.Pass : DoctorStatus.Failure,
                $"{version} at {node}; required >= {MinimumNodeVersion}");
        }
        catch (Exception exception)
        {
            Add("node", DoctorStatus.Failure, exception.Message);
        }
    }

    private void CheckPackageManagers()
    {
        var environment = new ManagedHarnessInstaller(new RunnerResolver(_logger), _logger).DetectEnvironment();
        var available = new List<string>();
        if (environment.NpmPath is not null) available.Add("npm");
        if (environment.NpxPath is not null) available.Add("npx");
        if (RunnerResolver.FindExecutable("pnpm") is not null) available.Add("pnpm");
        Add("package-managers", available.Count > 0 ? DoctorStatus.Pass : DoctorStatus.Failure,
            available.Count > 0 ? $"available: {string.Join(", ", available)}" : "npm, npx, and pnpm were not found on PATH");
    }

    private void CheckManagedHarness()
    {
        var root = ManagedHarnessPaths.GetRoot();
        if (!Directory.Exists(root))
        {
            Add("managed-harness", DoctorStatus.Pass, "not installed; an existing Harness or the first-run setup wizard can be used");
            return;
        }

        var installer = new ManagedHarnessInstaller(new RunnerResolver(_logger), _logger);
        var version = installer.ReadManagedVersion();
        var entry = ManagedHarnessPaths.GetPackageEntry();
        Add("managed-harness",
            version is not null && File.Exists(entry) ? DoctorStatus.Pass : DoctorStatus.Failure,
            version is not null && File.Exists(entry)
                ? $"{ManagedHarnessInstaller.PackageName} {version} at {root}"
                : $"managed directory exists but the package is incomplete: {root}");
    }

    private void CheckProfile(string dshHome)
    {
        var profileDirectory = Path.Combine(dshHome, "profiles", "web");
        var packageJson = Path.Combine(profileDirectory, "package.json");
        if (!Directory.Exists(profileDirectory))
        {
            Add("web-profile", DoctorStatus.Warning, $"profile directory does not exist yet: {profileDirectory}");
            return;
        }

        if (!File.Exists(packageJson))
        {
            Add("web-profile", DoctorStatus.Failure, $"package.json is missing from {profileDirectory}");
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(packageJson));
            var profile = document.RootElement.GetProperty("dsh").GetProperty("profile");
            var bundles = profile.GetProperty("bundles")
                .EnumerateArray()
                .Select(item => item.GetString())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .ToArray();
            if (bundles.Length == 0)
            {
                Add("web-profile", DoctorStatus.Failure, "dsh.profile.bundles is empty");
                return;
            }

            var patch = Path.Combine(profileDirectory, "cordis.patch.yml");
            Add("web-profile", File.Exists(patch) ? DoctorStatus.Pass : DoctorStatus.Failure,
                File.Exists(patch)
                    ? $"{bundles.Length} base bundle(s); package.json and cordis.patch.yml are present"
                    : "cordis.patch.yml is missing from the web profile");
        }
        catch (Exception exception)
        {
            Add("web-profile", DoctorStatus.Failure, $"invalid profile package.json: {exception.Message}");
        }
    }

    private void CheckBundlesAndRuntimeCopies(string dshHome)
    {
        var nodeModules = Path.Combine(dshHome, "profiles", "node_modules");
        if (!Directory.Exists(nodeModules))
        {
            Add("bundle-manifests", DoctorStatus.Warning, "profile node_modules is absent; install or start DSH once first");
            Add("runtime-singletons", DoctorStatus.Warning, "runtime package copies could not be inspected");
            return;
        }

        var packageRoots = EnumerateTopLevelPackages(nodeModules).ToArray();
        var bundles = new List<string>();
        var invalidBundles = new List<string>();
        foreach (var packageRoot in packageRoots)
        {
            var packageJson = Path.Combine(packageRoot, "package.json");
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(packageJson));
                if (!document.RootElement.TryGetProperty("dsh", out var dsh)
                    || !dsh.TryGetProperty("bundle", out var bundle)
                    || !bundle.TryGetProperty("patch", out var patchElement)
                    || patchElement.GetString() is not { Length: > 0 } patch)
                {
                    continue;
                }

                var name = document.RootElement.TryGetProperty("name", out var nameElement)
                    ? nameElement.GetString() ?? Path.GetFileName(packageRoot)
                    : Path.GetFileName(packageRoot);
                bundles.Add(name);
                var fullPatch = Path.GetFullPath(Path.Combine(packageRoot, patch));
                var rootWithSeparator = Path.GetFullPath(packageRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!fullPatch.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPatch))
                {
                    invalidBundles.Add(name);
                }
            }
            catch (Exception exception)
            {
                _logger.Error($"Could not inspect package manifest {packageJson}", exception);
            }
        }

        Add("bundle-manifests", invalidBundles.Count == 0 ? DoctorStatus.Pass : DoctorStatus.Failure,
            invalidBundles.Count == 0
                ? $"validated {bundles.Count} installed DSH bundle manifest(s)"
                : $"bundle patch is missing or escapes its package: {string.Join(", ", invalidBundles)}");

        var duplicates = new List<string>();
        foreach (var packageName in CoreRuntimePackages)
        {
            var paths = FindRuntimeCopies(nodeModules, packageRoots, packageName).ToArray();
            if (paths.Length > 1)
            {
                duplicates.Add($"@deepseek-ai/{packageName} ({paths.Length} copies: {string.Join(" | ", paths)})");
            }
        }

        Add("runtime-singletons", duplicates.Count == 0 ? DoctorStatus.Pass : DoctorStatus.Failure,
            duplicates.Count == 0
                ? "no duplicate Cordis/DSH runtime packages found in installed bundle roots"
                : string.Join("; ", duplicates));
    }

    private async Task CheckWebEndpointAsync(CancellationToken cancellationToken)
    {
        try
        {
            var probe = await WebHealthChecker.ProbeAsync(_config.WebUrl, TimeSpan.FromSeconds(2), cancellationToken)
                .ConfigureAwait(false);
            if (probe.Responding)
            {
                Add("web-endpoint", DoctorStatus.Pass, $"HTTP {probe.StatusCode} at {SanitizeUri(_config.WebUrl)}");
                return;
            }

            var portOccupied = _config.WebUrl.IsLoopback && IPGlobalProperties.GetIPGlobalProperties()
                .GetActiveTcpListeners()
                .Any(endpoint => endpoint.Port == _config.WebUrl.Port);
            Add("web-endpoint", portOccupied ? DoctorStatus.Failure : DoctorStatus.Warning,
                portOccupied
                    ? $"port {_config.WebUrl.Port} is listening, but the configured HTTP endpoint did not respond"
                    : "not responding; this is normal while DSH is stopped");
        }
        catch (Exception exception)
        {
            Add("web-endpoint", DoctorStatus.Failure, exception.Message);
        }
    }

    private void CheckLogDirectory()
    {
        try
        {
            Directory.CreateDirectory(_config.LogDirectory);
            var probe = Path.Combine(_config.LogDirectory, $".doctor-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probe, "dsh-launcher doctor");
            File.Delete(probe);
            Add("log-directory", DoctorStatus.Pass, $"writable: {_config.LogDirectory}");
        }
        catch (Exception exception)
        {
            Add("log-directory", DoctorStatus.Failure, exception.Message);
        }
    }

    private static string GetDshHome()
    {
        var configured = Environment.GetEnvironmentVariable("DSH_HOME");
        return string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dsh")
            : Path.GetFullPath(configured.Trim());
    }

    private static IEnumerable<string> EnumerateTopLevelPackages(string nodeModules)
    {
        foreach (var directory in Directory.EnumerateDirectories(nodeModules))
        {
            var name = Path.GetFileName(directory);
            if (name.StartsWith(".", StringComparison.Ordinal)) continue;
            if (name.StartsWith("@", StringComparison.Ordinal))
            {
                foreach (var scopedPackage in Directory.EnumerateDirectories(directory))
                {
                    if (File.Exists(Path.Combine(scopedPackage, "package.json"))) yield return scopedPackage;
                }
            }
            else if (File.Exists(Path.Combine(directory, "package.json")))
            {
                yield return directory;
            }
        }
    }

    private static IEnumerable<string> FindRuntimeCopies(
        string nodeModules,
        IReadOnlyList<string> packageRoots,
        string packageName)
    {
        var physicalPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var candidates = new[] { Path.Combine(nodeModules, "@deepseek-ai", packageName) }
            .Concat(packageRoots.Select(root => Path.Combine(root, "node_modules", "@deepseek-ai", packageName)));
        foreach (var candidate in candidates)
        {
            if (!File.Exists(Path.Combine(candidate, "package.json"))) continue;
            var info = new DirectoryInfo(candidate);
            var physical = info.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? info.FullName;
            if (physicalPaths.Add(physical)) yield return Redact(candidate);
        }
    }

    private void Add(string id, DoctorStatus status, string message)
    {
        _checks.Add(new DoctorCheck(id, status, Redact(message)));
    }

    private static string SanitizeUri(Uri uri)
    {
        var builder = new UriBuilder(uri)
        {
            UserName = string.Empty,
            Password = string.Empty,
            Query = string.Empty,
            Fragment = string.Empty
        };
        return builder.Uri.ToString();
    }

    private static string Redact(string value)
    {
        var withoutUriCredentials = UriCredentials.Replace(value, "$1<redacted>@");
        return SecretAssignment.Replace(withoutUriCredentials, "$1$2<redacted>");
    }
}
