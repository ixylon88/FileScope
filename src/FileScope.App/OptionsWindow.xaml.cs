using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using FileScope.App.ViewModels;

namespace FileScope.App;

public partial class OptionsWindow : Window
{
    private readonly OptionsViewModel _options;
    public OptionsWindow(OptionsViewModel options)
    {
        _options = options;
        InitializeComponent();
        DataContext = options;
        options.ColumnOrderChanged += OnColumnOrderChanged;
        options.PropertyChanged += OnOptionChanged;
        Closed += (_, _) =>
        {
            options.ColumnOrderChanged -= OnColumnOrderChanged;
            options.PropertyChanged -= OnOptionChanged;
        };
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
