using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FileScope.App.Behaviors;
using FileScope.App.ViewModels;

namespace FileScope.App;

public partial class MainWindow : Window
{
    private readonly OptionsViewModel _options;
    private readonly WindowPlacement _placement;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _options = viewModel.Options;
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
        if (args.OriginalSource is DependencyObject source
            && ItemsControl.ContainerFromElement(ResultsGrid, source) is DataGridRow { IsSelected: false } row)
        {
            ResultsGrid.UnselectAll();
            row.IsSelected = true;
        }
        UpdateSelectedRows();
    }

    private void UpdateSelectedRows()
    {
        if (DataContext is MainViewModel viewModel)
            viewModel.SetSelectedRows(ResultsGrid.SelectedItems.Cast<FileRowViewModel>());
    }

    private void OpenOptions(object sender, RoutedEventArgs args)
    {
        if (DataContext is MainViewModel viewModel)
            new OptionsWindow(viewModel.Options) { Owner = this }.ShowDialog();
    }

    private void OpenAbout(object sender, RoutedEventArgs args)
    {
        var dialog = AboutWindow.Create();
        dialog.Owner = this;
        dialog.ShowDialog();
    }

    private void OpenShortcuts(object sender, ExecutedRoutedEventArgs args)
    {
        args.Handled = true;
        new ShortcutsWindow { Owner = this }.ShowDialog();
    }

}
