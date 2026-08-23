using System.Text.Json;

namespace DshLauncher;

internal sealed record UpdatePreferences(bool AutoCheckUpdates, DateTimeOffset? LastUpdateCheckUtc)
{
    public static UpdatePreferences Default { get; } = new(AutoCheckUpdates: true, LastUpdateCheckUtc: null);
}

internal sealed class UpdatePreferencesStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _filePath;

    internal UpdatePreferencesStore(string filePath)
    {
        _filePath = filePath;
    }

    public static UpdatePreferencesStore CreateDefault()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "dsh-launcher");
        return new UpdatePreferencesStore(Path.Combine(directory, "preferences.json"));
    }

    public UpdatePreferences Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return UpdatePreferences.Default;
            }

            return JsonSerializer.Deserialize<UpdatePreferences>(File.ReadAllText(_filePath), JsonOptions)
                ?? UpdatePreferences.Default;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return UpdatePreferences.Default;
        }
    }

    public void Save(UpdatePreferences preferences)
    {
        var directory = Path.GetDirectoryName(_filePath)
            ?? throw new InvalidOperationException("The update preferences path has no parent directory.");
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
