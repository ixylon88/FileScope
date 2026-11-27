using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using FileScope.App.ViewModels;

namespace FileScope.App;

public partial class OptionsWindow : Window
{
    private readonly OptionsViewModel _options;
    private readonly WindowPlacement _placement;
    private bool _saveRequested;

    public RelayCommand SaveCommand => field ??= new(SaveAndClose);
    public RelayCommand CancelCommand => field ??= new(Close);
    internal bool ChangesSaved { get; private set; }

    public OptionsWindow(OptionsViewModel options)
    {
        _options = options;
        InitializeComponent();
        options.BeginEdit();
        DataContext = options;
        _placement = new WindowPlacement(this, options.OptionsWindowPreferences ?? new WindowPreferences(Width: 730, Height: 860, Maximized: false));
        options.ColumnOrderChanged += OnColumnOrderChanged;
        options.PropertyChanged += OnOptionChanged;
        Closed += (_, _) =>
        {
            options.ColumnOrderChanged -= OnColumnOrderChanged;
            options.PropertyChanged -= OnOptionChanged;
        };
    }

    private void SaveAndClose()
    {
        _saveRequested = true;
        try { Close(); }
        finally { _saveRequested = false; }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel) return;

        var placement = _placement.Capture();
        if (_saveRequested)
        {
            ChangesSaved = _options.TryCommitEdit(placement);
            e.Cancel = !ChangesSaved;
        }
        else
        {
            _options.CancelEdit(placement);
        }
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
