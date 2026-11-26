namespace FileScope.Ui.Tests;

[TestClass]
public sealed partial class UiTests
{
    [TestMethod]
    public Task CollapsibleMenu() =>
        WpfTestHost.RunAsync((app, strings) => CheckCollapsibleMenu(strings));

    [TestMethod]
    public Task ShortcutsDialog() =>
        WpfTestHost.RunAsync(CheckShortcutsDialog);

    [TestMethod]
    public Task PathPresentation() =>
        WpfTestHost.RunAsync((app, strings) => CheckPathPresentation(strings));

    [TestMethod]
    public Task FileMoves() =>
        WpfTestHost.RunAsync(CheckFileMoves);

    [TestMethod]
    public Task FileContextMenu() =>
        WpfTestHost.RunAsync(CheckFileContextMenu);

    [TestMethod]
    public Task AboutDialog() =>
        WpfTestHost.RunAsync(CheckAboutDialog);

    [TestMethod]
    public Task ThemeService() =>
        WpfTestHost.RunAsync((app, strings) => CheckThemeService(app));

    [TestMethod]
    public Task ThemeSwitching() =>
        WpfTestHost.RunAsync(CheckThemeSwitching);

    [TestMethod]
    public Task CheckboxPersistence() =>
        WpfTestHost.RunAsync((app, strings) => CheckCheckboxPersistence(strings));

    [TestMethod]
    public Task MetadataOptions() =>
        WpfTestHost.RunAsync((app, strings) => CheckMetadataOptions(strings));

    [TestMethod]
    public Task FolderAndExport() =>
        WpfTestHost.RunAsync((app, strings) => CheckFolderAndExport(strings));

    [TestMethod]
    public Task DroppedFolders() =>
        WpfTestHost.RunAsync((app, strings) => CheckDroppedFolders(strings));

    [TestMethod]
    public Task FileOperationCancellation() =>
        WpfTestHost.RunAsync((app, strings) => CheckFileOperationCancellation(strings));

    [TestMethod]
    public Task FileIntake() =>
        WpfTestHost.RunAsync((app, strings) => CheckFileIntake(strings));

    [TestMethod]
    public Task CatalogIntake() => WpfTestHost.RunAsync((app, strings) => CheckCatalogIntake(strings));

    [TestMethod]
    public Task IntakeFilter() =>
        WpfTestHost.RunAsync((app, strings) => CheckIntakeFilter(strings));

    [TestMethod]
    public Task IntakeFilterDuringScan() =>
        WpfTestHost.RunAsync((app, strings) => CheckIntakeFilterDuringScan(strings));

    [TestMethod]
    public Task BufferedProgress() =>
        WpfTestHost.RunAsync((app, strings) => CheckBufferedProgress(strings));

    [TestMethod]
    public Task RunSummaries() =>
        WpfTestHost.RunAsync((app, strings) => CheckRunSummaries(strings));

    [TestMethod]
    public Task Diagnostics() =>
        WpfTestHost.RunAsync((app, strings) => CheckDiagnostics(strings));

    [TestMethod]
    public Task MultipleSignatures() =>
        WpfTestHost.RunAsync((app, strings) => CheckMultipleSignatures(strings));

    [TestMethod]
    public Task ExceptValidFilter() =>
        WpfTestHost.RunAsync((app, strings) => CheckExceptValidFilter(strings));

    [TestMethod]
    public Task WindowPersistence() =>
        WpfTestHost.RunAsync((app, strings) => CheckWindowPersistence(strings));

    [TestMethod]
    public Task OptionsWindowPersistence() =>
        WpfTestHost.RunAsync((app, strings) => CheckOptionsWindowPersistence(strings));

    [TestMethod]
    public Task OptionsFooter() =>
        WpfTestHost.RunAsync((app, strings) => CheckOptionsFooter(strings));

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    public Task InitialFrozenColumns(int frozenCount) =>
        WpfTestHost.RunAsync((app, strings) => CheckInitialFrozenColumns(strings, frozenCount));

    [TestMethod]
    public Task HorizontalScrolling() =>
        WpfTestHost.RunAsync((app, strings) => CheckHorizontalScrolling(strings));

    [TestMethod]
    public Task TemplateCellBindings() =>
        WpfTestHost.RunAsync((app, strings) => CheckTemplateCellBindings(strings));

    [TestMethod]
    public Task ScrollBarSizing() =>
        WpfTestHost.RunAsync((app, strings) => CheckScrollBarSizing());

    [TestMethod]
    public Task MainWindowAnalysisAndFilters() =>
        WpfTestHost.RunAsync((app, strings) => CheckMainWindowAnalysisAndFilters(strings));

    [TestMethod]
    public Task CancellationDiscardsLateProgress() =>
        WpfTestHost.RunAsync((app, strings) => CheckCancellationDiscardsLateProgress(strings));

    [TestMethod]
    public Task CancelledBatchCanBeRetried() =>
        WpfTestHost.RunAsync((app, strings) => CheckCancelledBatchCanBeRetried(strings));
}
