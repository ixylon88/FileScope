using System.Text.Json;

namespace FileScope.App.Services;

public sealed record UserPreferences
{
    public string? Language { get; init; }
    public string? HashFormat { get; init; }
    public string[]? ColumnOrder { get; init; }
}

public interface IPreferencesStore
{
    UserPreferences Load();
    void Save(UserPreferences preferences);
}

public sealed class PreferencesStore(string path) : IPreferencesStore
{
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FileScope", "settings.json");

    public UserPreferences Load() => File.Exists(path)
        ? JsonSerializer.Deserialize<UserPreferences>(File.ReadAllText(path)) ?? new()
        : new();

    public void Save(UserPreferences preferences)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".settings-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(preferences,
                new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (IOException) { /* A cleanup failure must not replace the original error. */ }
            catch (UnauthorizedAccessException) { }
        }
    }
}
