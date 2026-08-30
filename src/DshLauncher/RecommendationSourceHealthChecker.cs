using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace DshLauncher;

internal enum RecommendationSourceHealthState
{
    Unchecked,
    Available,
    Warning,
    Unavailable,
}

internal sealed record RecommendationSourceHealth(
    RecommendationSourceHealthState State,
    string Detail,
    DateTimeOffset CheckedAtUtc);

internal sealed record RecommendationHealthProgress(int Completed, int Total, string ItemId);

internal sealed class RecommendationSourceHealthChecker : IDisposable
{
    private readonly HttpClient _client;
    private readonly bool _ownsClient;

    public RecommendationSourceHealthChecker(HttpClient? client = null)
    {
        _ownsClient = client is null;
        _client = client ?? new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
        });
        _client.Timeout = TimeSpan.FromSeconds(12);
        if (!_client.DefaultRequestHeaders.UserAgent.Any())
        {
            _client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("dsh-launcher", "0.5"));
        }
    }

    public async Task<IReadOnlyDictionary<string, RecommendationSourceHealth>> CheckAsync(
        IReadOnlyList<PluginRecommendation> recommendations,
        IProgress<RecommendationHealthProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<string, RecommendationSourceHealth>(StringComparer.OrdinalIgnoreCase);
        using var gate = new SemaphoreSlim(4);
        var completed = 0;
        var sync = new object();
        var tasks = recommendations.Select(async recommendation =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            RecommendationSourceHealth result;
            try
            {
                result = await CheckOneAsync(recommendation, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }

            lock (sync)
            {
                results[recommendation.Id] = result;
                completed++;
                progress?.Report(new RecommendationHealthProgress(
                    completed,
                    recommendations.Count,
                    recommendation.Id));
            }
        });
        await Task.WhenAll(tasks).ConfigureAwait(false);
        return results;
    }

    private async Task<RecommendationSourceHealth> CheckOneAsync(
        PluginRecommendation recommendation,
        CancellationToken cancellationToken)
    {
        var checkedAt = DateTimeOffset.UtcNow;
        if (recommendation.IsSkill)
        {
            var source = await ProbeAsync(new Uri(recommendation.RepositoryUrl), readBody: false, cancellationToken)
                .ConfigureAwait(false);
            return source.Success
                ? new RecommendationSourceHealth(
                    RecommendationSourceHealthState.Available,
                    "Pinned Skill path is reachable.",
                    checkedAt)
                : new RecommendationSourceHealth(
                    RecommendationSourceHealthState.Unavailable,
                    $"Pinned Skill path could not be reached: {source.Detail}",
                    checkedAt);
        }

        var manifestUri = TryBuildManifestUri(recommendation.RepositoryUrl);
        var manifest = manifestUri is null
            ? new ProbeResult(false, "Repository is not a supported GitHub root URL.", null)
            : await ProbeAsync(manifestUri, readBody: true, cancellationToken).ConfigureAwait(false);
        var bundlePatch = manifest.Success && manifest.Body is not null
            ? TryGetDshBundlePatch(manifest.Body)
            : null;
        var bundlePatchUri = manifestUri is null || bundlePatch is null
            ? null
            : TryBuildBundlePatchUri(manifestUri, bundlePatch);
        var patch = bundlePatchUri is null
            ? new ProbeResult(false, "Bundle patch path is missing or unsafe.", null)
            : await ProbeAsync(bundlePatchUri, readBody: false, cancellationToken).ConfigureAwait(false);
        var bundleVerified = manifest.Success && bundlePatch is not null && patch.Success;
        var installUri = BuildInstallSourceUri(recommendation);
        var install = await ProbeAsync(installUri, readBody: false, cancellationToken).ConfigureAwait(false);

        if (bundleVerified && install.Success)
        {
            return new RecommendationSourceHealth(
                RecommendationSourceHealthState.Available,
                "DSH bundle patch and pinned installation source are reachable.",
                checkedAt);
        }

        if (bundleVerified || install.Success)
        {
            var manifestDetail = bundleVerified
                ? "bundle patch OK"
                : $"bundle patch unavailable ({manifest.Detail}; {patch.Detail})";
            var installDetail = install.Success
                ? "install source OK"
                : $"install source unavailable ({install.Detail})";
            return new RecommendationSourceHealth(
                RecommendationSourceHealthState.Warning,
                $"{manifestDetail}; {installDetail}.",
                checkedAt);
        }

        return new RecommendationSourceHealth(
            RecommendationSourceHealthState.Unavailable,
            $"Bundle declaration and install source could not be verified: {manifest.Detail}; {install.Detail}.",
            checkedAt);
    }

    internal static Uri? TryBuildManifestUri(string repositoryUrl)
    {
        if (!Uri.TryCreate(repositoryUrl, UriKind.Absolute, out var repository) ||
            !string.Equals(repository.Host, "github.com", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var segments = repository.AbsolutePath.Trim('/').Split('/');
        if (segments.Length != 2)
        {
            return null;
        }

        var owner = Uri.EscapeDataString(segments[0]);
        var name = Uri.EscapeDataString(segments[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase)
            ? segments[1][..^4]
            : segments[1]);
        return new Uri($"https://raw.githubusercontent.com/{owner}/{name}/HEAD/package.json");
    }

    internal static Uri BuildInstallSourceUri(PluginRecommendation recommendation)
    {
        const string addPrefix = "dsh plugin --profile web add ";
        if (!recommendation.InstallCommand.StartsWith(addPrefix, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Unsupported plugin command for {recommendation.Id}.");
        }

        var firstArgument = recommendation.InstallCommand[addPrefix.Length..]
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        if (firstArgument is not null &&
            Uri.TryCreate(firstArgument, UriKind.Absolute, out var packageUri) &&
            packageUri.Scheme == Uri.UriSchemeHttps)
        {
            return packageUri;
        }

        var encodedName = Uri.EscapeDataString(recommendation.Name);
        var encodedVersion = Uri.EscapeDataString(recommendation.Version);
        return new Uri($"https://registry.npmjs.org/{encodedName}/{encodedVersion}");
    }

    internal static bool PackageDeclaresDshBundle(string packageJson)
    {
        return TryGetDshBundlePatch(packageJson) is not null;
    }

    internal static string? TryGetDshBundlePatch(string packageJson)
    {
        try
        {
            using var document = JsonDocument.Parse(packageJson);
            if (!document.RootElement.TryGetProperty("dsh", out var dsh) ||
                dsh.ValueKind != JsonValueKind.Object ||
                !dsh.TryGetProperty("bundle", out var bundle) ||
                bundle.ValueKind != JsonValueKind.Object ||
                !bundle.TryGetProperty("patch", out var patch) ||
                patch.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(patch.GetString()))
            {
                return null;
            }

            return patch.GetString();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static Uri? TryBuildBundlePatchUri(Uri packageManifestUri, string patchPath)
    {
        var normalized = patchPath.Trim().Replace('\\', '/');
        while (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        if (normalized.Length == 0 ||
            normalized.StartsWith("/", StringComparison.Ordinal) ||
            normalized.Split('/').Any(segment => segment is "" or "." or ".."))
        {
            return null;
        }

        return new Uri(packageManifestUri, normalized);
    }

    private async Task<ProbeResult> ProbeAsync(
        Uri uri,
        bool readBody,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            using var response = await _client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return new ProbeResult(false, $"HTTP {(int)response.StatusCode}", null);
            }

            var body = readBody
                ? await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)
                : null;
            return new ProbeResult(true, $"HTTP {(int)response.StatusCode}", body);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ProbeResult(false, "request timed out", null);
        }
        catch (HttpRequestException exception)
        {
            return new ProbeResult(false, exception.Message, null);
        }
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _client.Dispose();
        }
    }

    private sealed record ProbeResult(bool Success, string Detail, string? Body);
}
