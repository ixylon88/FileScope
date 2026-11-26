using System.Windows;
using DuplicateFinder.Core;

namespace DuplicateFinder.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        LocalizationService.Instance.UseApplicationResources(typeof(App).Assembly);
        base.OnStartup(e);

        var settings = new SettingsStore();
        var preferences = settings.Load();
        var strings = LocalizationService.Instance;
        var theme = new ThemeService(this);
        strings.SetCulture(preferences.Language);
        Dispatcher.VerifyAccess();
        theme.Apply(preferences.Theme);

        MainWindow = new MainWindow(new ResultsViewModel(), new DuplicateScanner(),
            new CleanupService(new WindowsRecycleBin()), preferences, settings, theme);
        MainWindow.Show();
    }
}
