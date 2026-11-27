using System.Globalization;
using System.Resources;
using System.Reflection;

namespace FileScope.Shared;

public sealed record LanguageOption(string CultureName, string NativeName);

public sealed class LocalizationService : ObservableObject
{
    private static readonly ResourceManager Shared = new("FileScope.Shared.Resources.Strings", typeof(LocalizationService).Assembly);
    private ResourceManager? _application;
    private static readonly Lazy<LocalizationService> Current = new(() => new LocalizationService());
    public static LocalizationService Instance => Current.Value;
    public event EventHandler? CultureChanged;
    public IReadOnlyList<LanguageOption> Languages { get; } = Array.AsReadOnly(
    [
        new LanguageOption("en", "English"), new LanguageOption("de", "Deutsch"),
        new LanguageOption("zh-Hans", "简体中文")
    ]);
    public CultureInfo Culture { get; private set; }
    public string this[string key] => _application?.GetString(key, Culture) ?? SharedText(key, Culture) ?? $"[{key}]";

    internal static string? SharedText(string key, CultureInfo culture) => Shared.GetString(key, culture);

    private LocalizationService()
    {
        var language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        Culture = CultureInfo.GetCultureInfo(language == "de" ? "de" : language == "zh" ? "zh-Hans" : "en");
        ApplyCulture();
    }

    // The composition root selects its own texts; tests can supply their fixture assembly.
    public void UseApplicationResources(Assembly assembly, string? baseName = null)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        _application = new ResourceManager(baseName ?? assembly.GetName().Name + ".Resources.Strings", assembly);
        OnPropertyChanged("Item[]");
    }

    public void SetCulture(string name)
    {
        if (Culture.Name == name) return;
        if (!Languages.Any(l => l.CultureName == name)) throw new ArgumentException("Unsupported UI language.", nameof(name));
        Culture = CultureInfo.GetCultureInfo(name);
        ApplyCulture();
        OnPropertyChanged(nameof(Culture));
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
