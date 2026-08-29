using System.Text.Json;

namespace DshLauncher;

internal sealed record RecommendationPreferences(
    string? LastPromptedVersion,
    string? SelectedProfileId)
{
    public static RecommendationPreferences Default { get; } = new(
        LastPromptedVersion: null,
        SelectedProfileId: null);

    public bool NeedsPrompt(string currentVersion)
    {
        return !string.Equals(LastPromptedVersion, currentVersion, StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed class RecommendationPreferencesStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _filePath;

    internal RecommendationPreferencesStore(string filePath)
    {
        _filePath = filePath;
    }

    public static RecommendationPreferencesStore CreateDefault()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "dsh-launcher");
        return new RecommendationPreferencesStore(Path.Combine(directory, "recommendations.json"));
    }

    public RecommendationPreferences Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return RecommendationPreferences.Default;
            }

            return JsonSerializer.Deserialize<RecommendationPreferences>(
                       File.ReadAllText(_filePath),
                       JsonOptions)
                   ?? RecommendationPreferences.Default;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return RecommendationPreferences.Default;
        }
    }

    public void Save(RecommendationPreferences preferences)
    {
        var directory = Path.GetDirectoryName(_filePath)
            ?? throw new InvalidOperationException("The recommendation preferences path has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = $"{_filePath}.{Environment.ProcessId}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(preferences, JsonOptions));
            File.Move(temporaryPath, _filePath, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }
}
