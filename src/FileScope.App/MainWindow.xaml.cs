using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using FileScope.App.Behaviors;
using FileScope.App.ViewModels;

namespace FileScope.App;

public partial class MainWindow : Window
{
    private readonly OptionsViewModel _options;
    private readonly WindowPlacement _placement;

    public RelayCommand OptionsCommand { get; }
    public RelayCommand AboutCommand { get; }

    public MainWindow(MainViewModel viewModel)
    {
        _options = viewModel.Options;
        OptionsCommand = new(OpenOptions);
        AboutCommand = new(OpenAbout);

        InitializeComponent();
        ShortcutsDialog.BindHelp(this, () => WindowPlacement.RememberDialogSize(ShortcutsWindow.Create(),
            _options.ShortcutsWindowPreferences, _options.SaveShortcutsWindowPreferences));
        DataContext = viewModel;
        _placement = new WindowPlacement(this, _options.WindowPreferences, centerOnWorkArea: true);
        Closed += (_, _) => viewModel.Dispose();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (!e.Cancel) _options.SaveWindowPreferences(_placement.Capture());
    }

    private void OnResultsGridLoaded(object sender, RoutedEventArgs args) => DataGridScrollFix.Apply(ResultsGrid);

    private void OnResultsSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        // Ignore selection events bubbling from the signature ComboBoxes.
        if (ReferenceEquals(args.OriginalSource, ResultsGrid)) UpdateSelectedRows();
    }

    private void OnResultsContextMenuOpening(object sender, ContextMenuEventArgs args)
    {
        FileRowContextMenu.SelectTargetRow(ResultsGrid, (DataGridRow)sender);
        UpdateSelectedRows();
    }

    private void UpdateSelectedRows()
    {
        if (DataContext is MainViewModel viewModel)
            viewModel.SetSelectedRows(ResultsGrid.SelectedItems.Cast<FileRowViewModel>());
    }

    private void OpenOptions()
    {
        var restoreWidths = ColumnLayoutBehavior.CaptureWidths(ResultsGrid);
        var dialog = new OptionsWindow(_options) { Owner = this };
        dialog.ShowDialog();
        if (!dialog.ChangesSaved) restoreWidths();
    }

    private void OpenAbout()
    {
        var dialog = WindowPlacement.RememberDialogSize(AboutWindow.Create(),
            _options.AboutWindowPreferences, _options.SaveAboutWindowPreferences);
        dialog.Owner = this;
        dialog.ShowDialog();
    }

}
