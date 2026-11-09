using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using FileScope.App.Behaviors;
using FileScope.App.Presentation;
using FileScope.App.Services;
using FileScope.App.ViewModels;

namespace FileScope.App;

public partial class MainWindow : Window
{
    private readonly OptionsViewModel _options;
    private WindowState _lastNonMinimizedState = WindowState.Normal;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _options = viewModel.Options;
        DataContext = viewModel;
        var workArea = SystemParameters.WorkArea; // Always the primary monitor.
        var size = WindowSize.Restore(_options.WindowPreferences, Width, Height,
            MinWidth, MinHeight, workArea.Width, workArea.Height);
        MinWidth = size.MinWidth;
        MinHeight = size.MinHeight;
        Width = size.Width;
        Height = size.Height;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = workArea.Left + (workArea.Width - Width) / 2;
        Top = workArea.Top + (workArea.Height - Height) / 2;
        if (_options.WindowPreferences?.Maximized == true)
        {
            _lastNonMinimizedState = WindowState.Maximized;
            WindowState = WindowState.Maximized;
        }
        Closed += (_, _) => viewModel.Dispose();
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        if (WindowState != WindowState.Minimized) _lastNonMinimizedState = WindowState;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel) return;
        // RestoreBounds retains the normal size while maximized/minimized.
        // It is empty if a window is closed before it has ever been shown.
        var bounds = RestoreBounds;
        var state = WindowState == WindowState.Minimized ? _lastNonMinimizedState : WindowState;
        _options.SaveWindowPreferences(new WindowPreferences(
            bounds.IsEmpty ? Width : bounds.Width,
            bounds.IsEmpty ? Height : bounds.Height,
            state == WindowState.Maximized));
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

    private void OpenAbout(object sender, RoutedEventArgs args) => new AboutWindow { Owner = this }.ShowDialog();
}
