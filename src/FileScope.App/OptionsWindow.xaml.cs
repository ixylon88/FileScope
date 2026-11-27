using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using FileScope.App.ViewModels;

namespace FileScope.App;

public partial class OptionsWindow : Window
{
    private readonly OptionsViewModel _options;
    private readonly WindowPlacement _placement;
    public OptionsWindow(OptionsViewModel options)
    {
        _options = options;
        InitializeComponent();
        DataContext = options;
        _placement = new WindowPlacement(this, options.OptionsWindowPreferences);
        options.ColumnOrderChanged += OnColumnOrderChanged;
        options.PropertyChanged += OnOptionChanged;
        Closed += (_, _) =>
        {
            options.ColumnOrderChanged -= OnColumnOrderChanged;
            options.PropertyChanged -= OnOptionChanged;
        };
    }
    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (!e.Cancel) _options.SaveOptionsWindowPreferences(_placement.Capture());
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
