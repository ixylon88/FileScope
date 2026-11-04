using System.Globalization;
using System.Resources;
using FileScope.App.Infrastructure;

namespace FileScope.App.Localization;

public sealed record LanguageOption(string CultureName, string NativeName);

public sealed class LocalizationService : ObservableObject
{
    private readonly ResourceManager _resources = new("FileScope.App.Resources.Strings", typeof(LocalizationService).Assembly);
    public static LocalizationService Instance { get; } = new();
    public event EventHandler? CultureChanged;
    public IReadOnlyList<LanguageOption> Languages { get; } = Array.AsReadOnly(
    [
        new LanguageOption("en", "English"), new LanguageOption("de", "Deutsch"),
        new LanguageOption("zh-Hans", "简体中文")
    ]);
    public CultureInfo Culture { get; private set; }
    public string this[string key] => _resources.GetString(key, Culture) ?? $"[{key}]";

    private LocalizationService()
    {
        var language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        Culture = CultureInfo.GetCultureInfo(language == "de" ? "de" : language == "zh" ? "zh-Hans" : "en");
        ApplyCulture();
    }

    public void SetCulture(string name)
    {
        if (Culture.Name == name) return;
        if (!Languages.Any(l => l.CultureName == name)) throw new ArgumentException("Unsupported UI language.", nameof(name));
        Culture = CultureInfo.GetCultureInfo(name);
        ApplyCulture();
        OnPropertyChanged("Item[]");
        CultureChanged?.Invoke(this, EventArgs.Empty);
    }

    public string Format(string key, params object?[] arguments) => string.Format(Culture, this[key], arguments);

    private void ApplyCulture()
    {
        CultureInfo.CurrentCulture = Culture;
        CultureInfo.CurrentUICulture = Culture;
        CultureInfo.DefaultThreadCurrentCulture = Culture;
        CultureInfo.DefaultThreadCurrentUICulture = Culture;
    }
}
