using System.IO;
using System.Text.Json;
using DuplicateFinder.Core;
using FileScope.App.Localization;
using FileScope.App.Services;
using FileScope.Core.Models;

namespace DuplicateFinder.App;

public sealed record Preferences
{
    public SearchRoot[] Roots { get; init; } = [];
    public string Language { get; init; } = LocalizationService.Instance.Culture.Name;
    public AppTheme Theme { get; init; }
    public ChecksumAlgorithm[] Hashes { get; init; } = [ChecksumAlgorithm.SHA256];
    public bool Embedded { get; init; } = true;
    public bool Catalogs { get; init; } = true;
    public bool Online { get; init; }

    [System.Text.Json.Serialization.JsonIgnore]
    public SearchOptions Search => new(Hashes, Embedded, Catalogs, Online ? RevocationMode.Online : RevocationMode.OfflineCache);
}

public sealed class SettingsStore
{
    private readonly string _path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DuplicateFinder", "settings.json");

    public Preferences Load()
    {
        try
        {
            using var input = File.OpenRead(_path);
            var settings = JsonSerializer.Deserialize<Preferences>(input);
            if (settings?.Roots == null || settings.Hashes == null || settings.Roots.Any(r => r == null || !Path.IsPathFullyQualified(r.Path))
                || !LocalizationService.Instance.Languages.Any(l => l.CultureName == settings.Language) || !Enum.IsDefined(settings.Theme))
            {
                return new();
            }

            return settings;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        { return new(); }
    }

    public void Save(Preferences settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var output = File.Create(temporary)) JsonSerializer.Serialize(output, settings);
            File.Move(temporary, _path, overwrite: true);
        }
        finally { File.Delete(temporary); } // Only our uniquely named settings scratch file.
    }
}
