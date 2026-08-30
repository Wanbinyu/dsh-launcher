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
    string Kind,
    string Name,
    string Version,
    string Publisher,
    string License,
    string DescriptionZh,
    string DescriptionEn,
    string ReasonZh,
    string ReasonEn,
    string Compatibility,
    string Requirements,
    string Privacy,
    string Network,
    string RepositoryUrl,
    string InstallCommand,
    string[] Profiles,
    string? InstalledPackageName = null)
{
    public bool IsSkill => string.Equals(Kind, "skill", StringComparison.OrdinalIgnoreCase);

    public bool IsOpenSource =>
        License.Contains("MIT", StringComparison.OrdinalIgnoreCase) ||
        License.Contains("Apache-2.0", StringComparison.OrdinalIgnoreCase) ||
        License.Contains("BSD-", StringComparison.OrdinalIgnoreCase) ||
        License.Contains("MPL-", StringComparison.OrdinalIgnoreCase);

    public string PackageNameForInspection => string.IsNullOrWhiteSpace(InstalledPackageName)
        ? Name
        : InstalledPackageName;
}

internal sealed record PluginRecommendationDocument(
    int SchemaVersion,
    RecommendationProfile[] Profiles,
    PluginRecommendation[] Items);

internal sealed class PluginRecommendationCatalog
{
    private const string ResourceName = "DshLauncher.Data.plugin-recommendations.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private PluginRecommendationCatalog(
        IReadOnlyList<RecommendationProfile> profiles,
        IReadOnlyList<PluginRecommendation> items)
    {
        Profiles = profiles;
        Items = items;
    }

    public IReadOnlyList<RecommendationProfile> Profiles { get; }

    public IReadOnlyList<PluginRecommendation> Items { get; }

    public IReadOnlyList<PluginRecommendation> Plugins =>
        Items.Where(item => !item.IsSkill).ToArray();

    public IReadOnlyList<PluginRecommendation> Skills =>
        Items.Where(item => item.IsSkill).ToArray();

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
        if (document.SchemaVersion != 2)
        {
            throw new InvalidDataException(
                $"Unsupported recommendation catalog schema: {document.SchemaVersion}.");
        }

        if (document.Profiles is not { Length: > 0 } || document.Items is not { Length: > 0 })
        {
            throw new InvalidDataException("The recommendation catalog must contain profiles and items.");
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

        var itemIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in document.Items)
        {
            var command = item.InstallCommand ?? string.Empty;
            var validKind = string.Equals(item.Kind, "plugin", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(item.Kind, "skill", StringComparison.OrdinalIgnoreCase);
            var validCommand = !command.Contains('\r') &&
                               !command.Contains('\n') &&
                               (item.IsSkill
                ? command.StartsWith(
                      "$env:DO_NOT_TRACK='1'; npx -y skills add https://github.com/",
                      StringComparison.Ordinal) &&
                  command.EndsWith(" -a universal --copy -y", StringComparison.Ordinal)
                : command.StartsWith("dsh plugin --profile web add ", StringComparison.Ordinal));
            var validInspectionName = item.InstalledPackageName is null ||
                                      (!item.IsSkill &&
                                       !string.IsNullOrWhiteSpace(item.InstalledPackageName) &&
                                       item.InstalledPackageName.All(character =>
                                           !char.IsWhiteSpace(character) && !char.IsControl(character)));
            if (string.IsNullOrWhiteSpace(item.Id) ||
                string.IsNullOrWhiteSpace(item.Name) ||
                string.IsNullOrWhiteSpace(item.Version) ||
                string.IsNullOrWhiteSpace(item.Publisher) ||
                string.IsNullOrWhiteSpace(item.License) ||
                string.IsNullOrWhiteSpace(item.DescriptionZh) ||
                string.IsNullOrWhiteSpace(item.ReasonZh) ||
                string.IsNullOrWhiteSpace(item.Compatibility) ||
                string.IsNullOrWhiteSpace(item.Requirements) ||
                string.IsNullOrWhiteSpace(item.Privacy) ||
                string.IsNullOrWhiteSpace(item.Network) ||
                !validKind ||
                !validInspectionName ||
                !itemIds.Add(item.Id) ||
                item.Profiles is not { Length: > 0 } ||
                item.Profiles.Any(profile => !profileIds.Contains(profile)) ||
                !Uri.TryCreate(item.RepositoryUrl, UriKind.Absolute, out var repository) ||
                repository.Scheme != Uri.UriSchemeHttps ||
                !validCommand)
            {
                throw new InvalidDataException($"The recommendation catalog contains an invalid item: {item.Id}.");
            }
        }

        return new PluginRecommendationCatalog(document.Profiles, document.Items);
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
        return Items.Where(item => item.Profiles.Contains(
                profileId,
                StringComparer.OrdinalIgnoreCase))
            .ToArray();
    }
}
