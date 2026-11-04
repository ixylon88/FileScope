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
    private readonly ResourceDictionary _paletteHost;
    public AppTheme CurrentTheme { get; private set; } = AppTheme.Light;

    public ThemeService(Application application)
    {
        _application = application;
        _paletteHost = application.Resources.MergedDictionaries.Single(dictionary =>
            dictionary.Source?.OriginalString.EndsWith("/ThemePalette.xaml", StringComparison.Ordinal) == true);
        Apply(CurrentTheme);
    }

    public void Apply(AppTheme theme)
    {
        _application.Dispatcher.VerifyAccess();
        if (!Enum.IsDefined(theme)) throw new ArgumentOutOfRangeException(nameof(theme));

        // Load before replacing: a failed resource load must keep the old palette.
        var palette = new ResourceDictionary
        {
            Source = new Uri($"/FileScope.App;component/Styles/Themes/{theme}.xaml", UriKind.Relative)
        };
        // ThemeMode updates native Fluent controls and window chrome as well.
        // WPF still marks the code API experimental in .NET 10; keep suppression local.
#pragma warning disable WPF0001
        _application.ThemeMode = theme == AppTheme.Dark ? ThemeMode.Dark : ThemeMode.Light;
#pragma warning restore WPF0001
        // Replace only our palette; retain control styles, converters and other resources.
        _paletteHost.MergedDictionaries[0] = palette;
        CurrentTheme = theme;
    }
}
