using System.Text.RegularExpressions;

namespace DshLauncher;

internal static class HarnessLaunchOutput
{
    private static readonly Regex AnsiEscapePattern = new(
        "\\u001B\\[[0-?]*[ -/]*[@-~]",
        RegexOptions.CultureInvariant);

    private static readonly Regex LaunchUrlPattern = new(
        @"(?:^|\s)dsh web:\s+(?<url>https?://[^\s()]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex TokenQueryPattern = new(
        @"(?<prefix>[?&]token=)[^&\s)\]}'""]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex BearerPattern = new(
        @"(?<prefix>\bBearer\s+)[^\s,;)\]}'""]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static Uri? TryGetAuthenticatedUrl(string line, Uri expectedWebUrl)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        var plainLine = AnsiEscapePattern.Replace(line, string.Empty);
        var match = LaunchUrlPattern.Match(plainLine);
        if (!match.Success ||
            !Uri.TryCreate(match.Groups["url"].Value, UriKind.Absolute, out var candidate) ||
            (!string.Equals(candidate.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(candidate.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) ||
            !string.IsNullOrEmpty(candidate.UserInfo) ||
            !candidate.IsLoopback ||
            !expectedWebUrl.IsLoopback ||
            !SameOrigin(candidate, expectedWebUrl) ||
            !TokenQueryPattern.IsMatch(candidate.Query))
        {
            return null;
        }

        return candidate;
    }

    public static string RedactSecrets(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var redacted = TokenQueryPattern.Replace(value, "${prefix}<redacted>");
        return BearerPattern.Replace(redacted, "${prefix}<redacted>");
    }

    private static bool SameOrigin(Uri candidate, Uri expected)
    {
        return string.Equals(candidate.Scheme, expected.Scheme, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(candidate.IdnHost, expected.IdnHost, StringComparison.OrdinalIgnoreCase) &&
               candidate.Port == expected.Port;
    }
}
