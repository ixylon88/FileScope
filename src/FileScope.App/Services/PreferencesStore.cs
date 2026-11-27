using System.Text.Json;

namespace FileScope.App.Services;

public sealed record UserPreferences
{
    public string? Theme { get; init; }
    public string? Language { get; init; }
    public string? HashFormat { get; init; }
    public string[]? ColumnOrder { get; init; }
    public bool PathAutoWidth { get; init; } = true;
    public string[]? ExportColumns { get; init; }
    public string? CsvDelimiter { get; init; }
    public WindowPreferences? Window { get; init; }
    public WindowPreferences? OptionsWindow { get; init; }
    public WindowPreferences? AboutWindow { get; init; }
    public WindowPreferences? ShortcutsWindow { get; init; }
    public AnalysisPreferences? Analysis { get; init; }
}

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
    public bool InspectCatalogs { get; init; }
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

    public void Save(UserPreferences preferences) => JsonSettingsFile.Save(path, preferences, SerializationOptions);

    private static JsonSerializerOptions SerializationOptions => field ??= new JsonSerializerOptions { WriteIndented = true };
}
