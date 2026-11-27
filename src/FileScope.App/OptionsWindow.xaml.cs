using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using FileScope.App.ViewModels;
using FileScope.App.Presentation;
using FileScope.App.Services;

namespace FileScope.App;

public partial class OptionsWindow : Window
{
    private readonly OptionsViewModel _options;
    private WindowState _lastNonMinimizedState = WindowState.Normal;
    public OptionsWindow(OptionsViewModel options)
    {
        _options = options;
        InitializeComponent();
        DataContext = options;
        var workArea = SystemParameters.WorkArea;
        var size = WindowSize.Restore(options.OptionsWindowPreferences, Width, Height,
            MinWidth, MinHeight, workArea.Width, workArea.Height);
        MinWidth = size.MinWidth;
        MinHeight = size.MinHeight;
        Width = size.Width;
        Height = size.Height;
        // Keep CenterOwner from XAML. Position is not part of these preferences.
        if (options.OptionsWindowPreferences?.Maximized == true)
        {
            _lastNonMinimizedState = WindowState.Maximized;
            WindowState = WindowState.Maximized;
        }
        options.ColumnOrderChanged += OnColumnOrderChanged;
        options.PropertyChanged += OnOptionChanged;
        Closed += (_, _) =>
        {
            options.ColumnOrderChanged -= OnColumnOrderChanged;
            options.PropertyChanged -= OnOptionChanged;
        };
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
        var bounds = RestoreBounds;
        var state = WindowState == WindowState.Minimized ? _lastNonMinimizedState : WindowState;
        _options.SaveOptionsWindowPreferences(new WindowPreferences(
            bounds.IsEmpty ? Width : bounds.Width,
            bounds.IsEmpty ? Height : bounds.Height,
            state == WindowState.Maximized));
    }

    private void OnColumnOrderChanged(object? sender, EventArgs args) => RevealSelection();
    private void OnOptionChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(OptionsViewModel.SelectedColumn)) RevealSelection();
    }
    private void RevealSelection() => Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
    {
        if (IsVisible && _options.SelectedColumn is { } selected) ColumnList.ScrollIntoView(selected);
    }));
}
