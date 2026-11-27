using System.IO;
using System.Text.Json;
using DuplicateFinder.App.Presentation;
using DuplicateFinder.Core.Models;
using FileScope.Core.Models;

namespace DuplicateFinder.App.Services;

public sealed record Preferences
{
    public SearchRoot[] Roots { get; init; } = [];
    public string Language { get; init; } = LocalizationService.Instance.Culture.Name;
    public AppTheme Theme { get; init; }
    public HashFormat HashFormat { get; init; } = HashFormat.HexUpper;
    public bool PathAutoWidth { get; init; } = true;
    public WindowPreferences? MainWindow { get; init; }
    public WindowPreferences? OptionsWindow { get; init; }
    public WindowPreferences? AboutWindow { get; init; }
    public WindowPreferences? ShortcutsWindow { get; init; }
    public ChecksumAlgorithm[] Hashes { get; init; } = [ChecksumAlgorithm.SHA256];
    public bool Embedded { get; init; } = true;
    public bool Catalogs { get; init; } = true;
    public bool Online { get; init; }
    public FileSizeRange SizeRange { get; init; }
    public FileSizeUnit MinimumSizeUnit { get; init; } = FileSizeUnit.Bytes;
    public FileSizeUnit MaximumSizeUnit { get; init; } = FileSizeUnit.Bytes;

    [System.Text.Json.Serialization.JsonIgnore]
    public SearchOptions Search => new(Hashes, Embedded, Catalogs, Online ? RevocationMode.Online : RevocationMode.OfflineCache)
    {
        SizeRange = SizeRange
    };
}

public sealed class SettingsStore
{
    private readonly string _path;

    public SettingsStore() : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DuplicateFinder", "settings.json"))
    { }

    internal SettingsStore(string path) => _path = path;

    public Preferences Load()
    {
        try
        {
            using var input = File.OpenRead(_path);
            var settings = JsonSerializer.Deserialize<Preferences>(input);
            if (settings?.Roots == null || settings.Hashes == null || settings.Roots.Any(r => r == null || !Path.IsPathFullyQualified(r.Path))
                || !LocalizationService.Instance.Languages.Any(l => l.CultureName == settings.Language) || !Enum.IsDefined(settings.Theme)
                || !Enum.IsDefined(settings.MinimumSizeUnit) || !Enum.IsDefined(settings.MaximumSizeUnit))
            {
                return new();
            }

            return Enum.IsDefined(settings.HashFormat) ? settings : settings with { HashFormat = HashFormat.HexUpper };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        { return new(); }
    }

    public void Save(Preferences settings) => JsonSettingsFile.Save(_path, settings);
}
