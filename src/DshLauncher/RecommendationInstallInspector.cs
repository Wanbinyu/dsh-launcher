using System.Text.Json;

namespace DshLauncher;

internal enum RecommendationInstallState
{
    Unknown,
    WorkspaceDependent,
    NotInstalled,
    InstalledCurrent,
    InstalledDifferent,
}

internal sealed record RecommendationInstallStatus(
    RecommendationInstallState State,
    string? InstalledVersion = null,
    string? Detail = null);

internal sealed class RecommendationInstallInspector
{
    private readonly Func<CancellationToken, Task<RunnerSpec>> _resolveRunner;
    private readonly LauncherLogger _logger;

    public RecommendationInstallInspector(
        Func<CancellationToken, Task<RunnerSpec>> resolveRunner,
        LauncherLogger logger)
    {
        _resolveRunner = resolveRunner;
        _logger = logger;
    }

    public async Task<IReadOnlyDictionary<string, RecommendationInstallStatus>> InspectAsync(
        IReadOnlyList<PluginRecommendation> recommendations,
        CancellationToken cancellationToken = default)
    {
        var statuses = recommendations.ToDictionary(
            recommendation => recommendation.Id,
            recommendation => recommendation.IsSkill
                ? new RecommendationInstallStatus(
                    RecommendationInstallState.WorkspaceDependent,
                    Detail: "Current workspace must be checked by Harness.")
                : new RecommendationInstallStatus(RecommendationInstallState.Unknown),
            StringComparer.OrdinalIgnoreCase);

        try
        {
            var runner = await _resolveRunner(cancellationToken).ConfigureAwait(false);
            var listRunner = runner with
            {
                PrefixArguments = runner.PrefixArguments.Concat(new[]
                {
                    "plugin",
                    "--profile",
                    "web",
                    "list",
                    "--depth",
                    "0",
                    "--json",
                }).ToArray(),
            };
            var result = await ProcessLauncher.CaptureAsync(listRunner, cancellationToken).ConfigureAwait(false);
            if (result.ExitCode != 0)
            {
                var detail = FirstNonEmptyLine(result.StandardError) ??
                             FirstNonEmptyLine(result.StandardOutput) ??
                             $"dsh plugin list exited with code {result.ExitCode}.";
                return MarkPluginStatusesUnknown(recommendations, statuses, detail);
            }

            var installed = ParseInstalledPackages(result.StandardOutput);
            foreach (var recommendation in recommendations.Where(item => !item.IsSkill))
            {
                if (!installed.TryGetValue(recommendation.PackageNameForInspection, out var installedVersion))
                {
                    statuses[recommendation.Id] = new RecommendationInstallStatus(
                        RecommendationInstallState.NotInstalled);
                    continue;
                }

                statuses[recommendation.Id] = string.Equals(
                    installedVersion,
                    recommendation.Version,
                    StringComparison.OrdinalIgnoreCase)
                    ? new RecommendationInstallStatus(
                        RecommendationInstallState.InstalledCurrent,
                        installedVersion)
                    : new RecommendationInstallStatus(
                        RecommendationInstallState.InstalledDifferent,
                        installedVersion);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or
                                          InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _logger.Error("Could not inspect recommended plugin installation status", exception);
            return MarkPluginStatusesUnknown(recommendations, statuses, exception.Message);
        }

        return statuses;
    }

    internal static IReadOnlyDictionary<string, string> ParseInstalledPackages(string json)
    {
        using var document = JsonDocument.Parse(json);
        var installed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var roots = document.RootElement.ValueKind == JsonValueKind.Array
            ? document.RootElement.EnumerateArray().ToArray()
            : new[] { document.RootElement };
        foreach (var root in roots)
        {
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("dependencies", out var dependencies) ||
                dependencies.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            foreach (var dependency in dependencies.EnumerateObject())
            {
                if (dependency.Value.ValueKind != JsonValueKind.Object ||
                    !dependency.Value.TryGetProperty("version", out var version) ||
                    version.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(version.GetString()))
                {
                    continue;
                }

                installed[dependency.Name] = version.GetString()!;
            }
        }

        return installed;
    }

    private static IReadOnlyDictionary<string, RecommendationInstallStatus> MarkPluginStatusesUnknown(
        IReadOnlyList<PluginRecommendation> recommendations,
        Dictionary<string, RecommendationInstallStatus> statuses,
        string detail)
    {
        foreach (var recommendation in recommendations.Where(item => !item.IsSkill))
        {
            statuses[recommendation.Id] = new RecommendationInstallStatus(
                RecommendationInstallState.Unknown,
                Detail: detail);
        }

        return statuses;
    }

    private static string? FirstNonEmptyLine(string text)
    {
        return text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.Length > 0);
    }
}
