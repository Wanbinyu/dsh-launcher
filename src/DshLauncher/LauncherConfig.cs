using System.Security.Cryptography;
using System.Text;

namespace DshLauncher;

internal sealed class LauncherConfig
{
    private LauncherConfig(Uri webUrl, bool autoOpen, TimeSpan startupTimeout, string logDirectory)
    {
        WebUrl = webUrl;
        AutoOpen = autoOpen;
        StartupTimeout = startupTimeout;
        LogDirectory = logDirectory;
    }

    public Uri WebUrl { get; }

    public bool AutoOpen { get; }

    public TimeSpan StartupTimeout { get; }

    public string LogDirectory { get; }

    public string PipeName => GetUserScopedName("control");

    public string MutexName => GetUserScopedName("instance");

    public static LauncherConfig Load()
    {
        var webUrl = GetWebUrl();
        var autoOpen = GetBoolean("DSH_AUTO_OPEN", defaultValue: true);
        var timeoutSeconds = GetInteger("DSH_START_TIMEOUT_SECONDS", defaultValue: 30, minimum: 1, maximum: 300);
        var configuredLogDirectory = Environment.GetEnvironmentVariable("DSH_LOG_DIR");
        var logDirectory = string.IsNullOrWhiteSpace(configuredLogDirectory)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "dsh-launcher",
                "logs")
            : Path.GetFullPath(configuredLogDirectory.Trim());

        Directory.CreateDirectory(logDirectory);
        return new LauncherConfig(webUrl, autoOpen, TimeSpan.FromSeconds(timeoutSeconds), logDirectory);
    }

    private static Uri GetWebUrl()
    {
        var configuredUrl = Environment.GetEnvironmentVariable("DSH_WEB_URL");
        if (!string.IsNullOrWhiteSpace(configuredUrl))
        {
            if (!Uri.TryCreate(configuredUrl.Trim(), UriKind.Absolute, out var parsedUrl) ||
                (parsedUrl.Scheme != Uri.UriSchemeHttp && parsedUrl.Scheme != Uri.UriSchemeHttps))
            {
                throw new InvalidOperationException($"DSH_WEB_URL must be an absolute HTTP(S) URL: {configuredUrl}");
            }

            return parsedUrl;
        }

        var port = GetInteger("DSH_WEB_PORT", defaultValue: 3080, minimum: 1, maximum: 65535);
        return new Uri($"http://127.0.0.1:{port}/", UriKind.Absolute);
    }

    private static bool GetBoolean(string name, bool defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        return value.Trim().ToLowerInvariant() is not ("0" or "false" or "no" or "off");
    }

    private static int GetInteger(string name, int defaultValue, int minimum, int maximum)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        if (!int.TryParse(value.Trim(), out var parsed) || parsed < minimum || parsed > maximum)
        {
            throw new InvalidOperationException($"{name} must be an integer from {minimum} to {maximum}: {value}");
        }

        return parsed;
    }

    private static string GetUserScopedName(string suffix)
    {
        var user = $"{Environment.UserDomainName}\\{Environment.UserName}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(user));
        var userHash = Convert.ToHexString(bytes.AsSpan(0, 8)).ToLowerInvariant();
        return $"dsh-launcher-{userHash}-{suffix}";
    }
}
