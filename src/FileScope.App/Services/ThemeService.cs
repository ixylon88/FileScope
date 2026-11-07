using System.Windows;

namespace FileScope.App.Services;

public sealed class ThemeService : IThemeService
{
    private readonly Application _application;
    private bool _initialized;
    public AppTheme CurrentTheme { get; private set; } = AppTheme.Light;

    public ThemeService(Application application)
    {
        ArgumentNullException.ThrowIfNull(application);
        _application = application;
        _application.Dispatcher.VerifyAccess();
        CurrentTheme = GetApplicationThemeMode();
        // Even an unchanged native mode needs a matching application palette on first use.
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
        _application.Dispatcher.VerifyAccess();
        if (!Enum.IsDefined(theme)) throw new ArgumentOutOfRangeException(nameof(theme));
        if (_initialized && CurrentTheme == theme) return;

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
        }
        else if (!_application.Resources.MergedDictionaries.Any(dictionary => dictionary.Source?.OriginalString.EndsWith($"{theme}ThemePalette.xaml", StringComparison.Ordinal) == true))
        {
            var index = _application.Resources.MergedDictionaries.IndexOf(paletteHost);
            _application.Resources.MergedDictionaries[index] = palette;
        }

        SetApplicationThemeMode(theme);

        CurrentTheme = theme;
        _initialized = true;
    }

    // ThemeMode updates native Fluent controls and window chrome as well.
    // WPF still marks the code API experimental in .NET 10; keep suppression local.
#pragma warning disable WPF0001
    private void SetApplicationThemeMode(AppTheme theme) {
        var newTheme = theme == AppTheme.Dark ? ThemeMode.Dark : ThemeMode.Light;
        if (newTheme != _application.ThemeMode)
            _application.ThemeMode = newTheme;
    }
    private AppTheme GetApplicationThemeMode() => _application.ThemeMode == ThemeMode.Dark ? AppTheme.Dark : AppTheme.Light;
#pragma warning restore WPF0001
}
