using System.Net.Http.Headers;
using System.Text.Json;

namespace DshLauncher;

internal sealed record UpdateCheckResult(
    Version CurrentVersion,
    Version LatestVersion,
    string LatestTag,
    Uri ReleaseUri)
{
    public bool IsUpdateAvailable => LatestVersion > CurrentVersion;
}

internal sealed class UpdateChecker : IDisposable
{
    private static readonly Uri LatestReleaseApi = new(
        "https://api.github.com/repos/Wanbinyu/dsh-launcher/releases/latest");
    private readonly HttpClient _client;
    private readonly bool _ownsClient;

    public UpdateChecker()
        : this(CreateClient(), ownsClient: true)
    {
    }

    internal UpdateChecker(HttpClient client)
        : this(client, ownsClient: false)
    {
    }

    private UpdateChecker(HttpClient client, bool ownsClient)
    {
        _client = client;
        _ownsClient = ownsClient;
    }

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApi);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

        using var response = await _client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var current = NormalizeVersion(typeof(UpdateChecker).Assembly.GetName().Version ?? new Version(0, 0, 0));
        return ParseLatestRelease(json, current);
    }

    internal static UpdateCheckResult ParseLatestRelease(string json, Version currentVersion)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("tag_name", out var tagElement))
        {
            throw new InvalidDataException("GitHub release response did not contain tag_name.");
        }

        var tag = tagElement.GetString()?.Trim();
        if (string.IsNullOrWhiteSpace(tag) || !TryParseReleaseVersion(tag, out var latest))
        {
            throw new InvalidDataException($"GitHub returned an invalid release tag: {tag ?? "<empty>"}");
        }

        var releaseUri = new Uri(
            $"https://github.com/Wanbinyu/dsh-launcher/releases/tag/{Uri.EscapeDataString(tag)}");
        return new UpdateCheckResult(
            NormalizeVersion(currentVersion),
            latest,
            tag,
            releaseUri);
    }

    private static bool TryParseReleaseVersion(string tag, out Version version)
    {
        var value = tag.StartsWith('v') ? tag[1..] : tag;
        value = value.Split('-', '+')[0];
        if (!Version.TryParse(value, out var parsed))
        {
            version = new Version(0, 0, 0);
            return false;
        }

        version = NormalizeVersion(parsed);
        return true;
    }

    private static Version NormalizeVersion(Version version)
    {
        return new Version(version.Major, version.Minor, Math.Max(version.Build, 0));
    }

    private static HttpClient CreateClient()
    {
        var version = NormalizeVersion(typeof(UpdateChecker).Assembly.GetName().Version ?? new Version(0, 0, 0));
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(8),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"dsh-launcher-update-check/{version}");
        return client;
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _client.Dispose();
        }
    }
}
