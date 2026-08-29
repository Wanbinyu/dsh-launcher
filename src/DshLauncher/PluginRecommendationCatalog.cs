using System.Reflection;
using System.Text.Json;

namespace DshLauncher;

internal sealed record RecommendationProfile(
    string Id,
    string NameZh,
    string NameEn,
    string SummaryZh,
    string SummaryEn);

internal sealed record PluginRecommendation(
    string Id,
    string Name,
    string Version,
    string DescriptionZh,
    string DescriptionEn,
    string ReasonZh,
    string ReasonEn,
    string Compatibility,
    string Privacy,
    string Network,
    string RepositoryUrl,
    string InstallCommand,
    string[] Profiles);

internal sealed record PluginRecommendationDocument(
    int SchemaVersion,
    RecommendationProfile[] Profiles,
    PluginRecommendation[] Plugins);

internal sealed class PluginRecommendationCatalog
{
    private const string ResourceName = "DshLauncher.Data.plugin-recommendations.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private PluginRecommendationCatalog(
        IReadOnlyList<RecommendationProfile> profiles,
        IReadOnlyList<PluginRecommendation> plugins)
    {
        Profiles = profiles;
        Plugins = plugins;
    }

    public IReadOnlyList<RecommendationProfile> Profiles { get; }

    public IReadOnlyList<PluginRecommendation> Plugins { get; }

    public static PluginRecommendationCatalog LoadEmbedded()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
        if (stream is null)
        {
            throw new InvalidOperationException($"Missing embedded recommendation catalog: {ResourceName}");
        }

        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    internal static PluginRecommendationCatalog Parse(string json)
    {
        var document = JsonSerializer.Deserialize<PluginRecommendationDocument>(json, JsonOptions)
            ?? throw new InvalidDataException("The recommendation catalog is empty.");
        if (document.SchemaVersion != 1)
        {
            throw new InvalidDataException(
                $"Unsupported recommendation catalog schema: {document.SchemaVersion}.");
        }

        if (document.Profiles.Length == 0 || document.Plugins.Length == 0)
        {
            throw new InvalidDataException("The recommendation catalog must contain profiles and plugins.");
        }

        var profileIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in document.Profiles)
        {
            if (string.IsNullOrWhiteSpace(profile.Id) ||
                string.IsNullOrWhiteSpace(profile.NameZh) ||
                !profileIds.Add(profile.Id))
            {
                throw new InvalidDataException("The recommendation catalog contains an invalid profile.");
            }
        }

        var pluginIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var plugin in document.Plugins)
        {
            if (string.IsNullOrWhiteSpace(plugin.Id) ||
                string.IsNullOrWhiteSpace(plugin.Name) ||
                !pluginIds.Add(plugin.Id) ||
                plugin.Profiles.Length == 0 ||
                plugin.Profiles.Any(profile => !profileIds.Contains(profile)) ||
                !Uri.TryCreate(plugin.RepositoryUrl, UriKind.Absolute, out var repository) ||
                repository.Scheme != Uri.UriSchemeHttps ||
                !plugin.InstallCommand.StartsWith("dsh plugin --profile web add https://", StringComparison.Ordinal))
            {
                throw new InvalidDataException($"The recommendation catalog contains an invalid plugin: {plugin.Id}.");
            }
        }

        return new PluginRecommendationCatalog(document.Profiles, document.Plugins);
    }

    public RecommendationProfile ResolveProfile(string? profileId)
    {
        return Profiles.FirstOrDefault(profile => string.Equals(
                   profile.Id,
                   profileId,
                   StringComparison.OrdinalIgnoreCase))
               ?? Profiles[0];
    }

    public IReadOnlyList<PluginRecommendation> ForProfile(string profileId)
    {
        return Plugins.Where(plugin => plugin.Profiles.Contains(
                profileId,
                StringComparer.OrdinalIgnoreCase))
            .ToArray();
    }
}
