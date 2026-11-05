using System.Windows;
using FileScope.App.Localization;
using FileScope.App.Services;
using FileScope.App.ViewModels;
using FileScope.Core.Checksums;
using FileScope.Core.Services;
using FileScope.Core.Signatures;
using FileScope.Core.Metadata;
using FileScope.Core.Files;
using FileScope.Core.Export;

namespace FileScope.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Composition root: concrete dependencies are assembled only here.
        var checksumService = new StreamChecksumService(new ChecksumStrategyFactory());
        var analysis = new FileAnalysisService(checksumService, new WindowsAuthenticodeInspector(),
            new FilePropertiesInspector(), new PeHeaderInspector());
        var strings = LocalizationService.Instance;
        var options = new OptionsViewModel(strings, new PreferencesStore(PreferencesStore.DefaultPath));
        var viewModel = new MainViewModel(analysis, new FilePickerService(), strings, options,
            new FolderScanner(), new ResultExporter([new CsvExportStrategy(), new JsonExportStrategy()]));
        var window = new MainWindow(viewModel);
        MainWindow = window;
        window.Show();
    }
}
