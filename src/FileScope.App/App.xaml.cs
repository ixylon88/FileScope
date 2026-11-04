using System.Windows;
using FileScope.App.Localization;
using FileScope.App.Services;
using FileScope.App.ViewModels;
using FileScope.Core.Checksums;
using FileScope.Core.Services;
using FileScope.Core.Signatures;

namespace FileScope.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Composition root: concrete dependencies are assembled only here.
        var checksumService = new StreamChecksumService(new ChecksumStrategyFactory());
        var analysis = new FileAnalysisService(checksumService, new WindowsAuthenticodeInspector());
        var viewModel = new MainViewModel(analysis, new FilePickerService(), LocalizationService.Instance,
            new ThemeService(this));
        var window = new MainWindow(viewModel);
        MainWindow = window;
        window.Show();
    }
}
