using System.Windows;

namespace FileScope.App.Services;

public enum AppTheme { Light, Dark }

public interface IThemeService
{
    AppTheme CurrentTheme { get; }
    void Apply(AppTheme theme);
}

public sealed class ThemeService : IThemeService
{
    private readonly Application _application;
    public AppTheme CurrentTheme { get; private set; } = AppTheme.Light;

    public ThemeService(Application application)
    {
        _application = application;
        CurrentTheme = GetApplicationThemeMode();
        Apply(CurrentTheme);
    }

    /// <summary>
    /// Apply a new theme.
    /// </summary>
    /// <remarks>
    /// Idempotent method.
    /// </remarks>
    /// <param name="theme">The new theme to apply.</param>
    /// <exception cref="ArgumentOutOfRangeException">An unsupported theme value is provided.</exception>
    public void Apply(AppTheme theme)
    {
        if (CurrentTheme == theme) return;

        _application.Dispatcher.VerifyAccess();
        if (!Enum.IsDefined(theme)) throw new ArgumentOutOfRangeException(nameof(theme));

        // Load before replacing: a failed resource load must keep the old palette.
        var palette = new ResourceDictionary
        {
            Source = new Uri($"/FileScope.App;component/Styles/Themes/{theme}ThemePalette.xaml", UriKind.Relative)
        };

        // Replace only our palette; retain control styles, converters and other resources.
        var paletteHost = _application.Resources.MergedDictionaries.SingleOrDefault(dictionary => dictionary.Source?.OriginalString.EndsWith("ThemePalette.xaml", StringComparison.Ordinal) == true);
        if (paletteHost == null)
        {
            _application.Resources.MergedDictionaries.Add(palette);
            CurrentTheme = theme;
            return;
        }
        var index = _application.Resources.MergedDictionaries.IndexOf(paletteHost);
        _application.Resources.MergedDictionaries[index] = palette;

        SetApplicationThemeMode(theme);

        CurrentTheme = theme;
    }

// ThemeMode updates native Fluent controls and window chrome as well.
// WPF still marks the code API experimental in .NET 10; keep suppression local.
#pragma warning disable WPF0001
    private void SetApplicationThemeMode(AppTheme theme) => _application.ThemeMode = theme == AppTheme.Dark ? ThemeMode.Dark : ThemeMode.Light;
    private AppTheme GetApplicationThemeMode() => _application.ThemeMode == ThemeMode.Dark ? AppTheme.Dark : AppTheme.Light;
#pragma warning restore WPF0001
}
