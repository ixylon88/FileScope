using System.Text.Json;

namespace FileScope.App.Services;

public sealed record UserPreferences
{
    public string? Language { get; init; }
    public string? HashFormat { get; init; }
    public string[]? ColumnOrder { get; init; }
    public string[]? ExportColumns { get; init; }
    public string? CsvDelimiter { get; init; }
    public WindowPreferences? Window { get; init; }
    public AnalysisPreferences? Analysis { get; init; }
}

public sealed record WindowPreferences(double Width, double Height, bool Maximized);

// Defaults apply only when settings/individual fields have not been saved yet.
// Explicit false values, including all checks disabled, must survive a restart.
public sealed record AnalysisPreferences
{
    public bool MD5 { get; init; }
    public bool SHA1 { get; init; }
    public bool SHA256 { get; init; } = true;
    public bool SHA384 { get; init; }
    public bool SHA512 { get; init; }
    public bool InspectCertificates { get; init; } = true;
    public bool OnlineRevocation { get; init; }
    public bool InspectFileProperties { get; init; }
    public bool InspectPeHeader { get; init; }
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
            File.WriteAllText(temporary, JsonSerializer.Serialize(preferences, SerializationOptions));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (IOException) { /* A cleanup failure must not replace the original error. */ }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static JsonSerializerOptions SerializationOptions => field ??= new JsonSerializerOptions { WriteIndented = true };
}
