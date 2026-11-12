using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FileScope.App.ViewModels;

namespace FileScope.App.Behaviors;

// Only this view adapter knows DataGridColumn. Options store stable IDs, never controls.
public static class ColumnLayoutBehavior
{
    public static readonly DependencyProperty IdProperty = DependencyProperty.RegisterAttached(
        "Id", typeof(string), typeof(ColumnLayoutBehavior), new PropertyMetadata(""));
    public static string GetId(DependencyObject target) => (string)target.GetValue(IdProperty);
    public static void SetId(DependencyObject target, string value) => target.SetValue(IdProperty, value);

    public static readonly DependencyProperty OptionsProperty = DependencyProperty.RegisterAttached(
        "Options", typeof(OptionsViewModel), typeof(ColumnLayoutBehavior), new PropertyMetadata(null, OnOptionsChanged));
    public static OptionsViewModel? GetOptions(DependencyObject target) => (OptionsViewModel?)target.GetValue(OptionsProperty);
    public static void SetOptions(DependencyObject target, OptionsViewModel? value) => target.SetValue(OptionsProperty, value);

    public static readonly DependencyProperty FitRevisionProperty = DependencyProperty.RegisterAttached(
        "FitRevision", typeof(int), typeof(ColumnLayoutBehavior), new PropertyMetadata(0, OnFitRequested));
    public static int GetFitRevision(DependencyObject target) => (int)target.GetValue(FitRevisionProperty);
    public static void SetFitRevision(DependencyObject target, int value) => target.SetValue(FitRevisionProperty, value);

    private static readonly DependencyProperty ControllerProperty = DependencyProperty.RegisterAttached(
        "Controller", typeof(Controller), typeof(ColumnLayoutBehavior));
    private static void OnOptionsChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not DataGrid grid) return;
        (grid.GetValue(ControllerProperty) as Controller)?.Dispose();
#pragma warning disable CA2000 // Dispose objects before losing scope: we dispose on new assignments (line above)
        grid.SetValue(ControllerProperty, args.NewValue is OptionsViewModel options ? new Controller(grid, options) : null);
#pragma warning restore CA2000 // Dispose objects before losing scope
    }
    private static void OnFitRequested(DependencyObject target, DependencyPropertyChangedEventArgs args) =>
        (target.GetValue(ControllerProperty) as Controller)?.RequestFit();

    private sealed class Controller : IDisposable
    {
        private readonly DataGrid _grid;
        private readonly OptionsViewModel _options;
        private bool _connected, _applying;
        private DispatcherOperation? _pendingFit;

        public Controller(DataGrid grid, OptionsViewModel options)
        {
            _grid = grid;
            _options = options;
            grid.Loaded += OnLoaded;
            grid.Unloaded += OnUnloaded;
            Connect();
        }
        private void OnLoaded(object sender, RoutedEventArgs args) => Connect();
        private void OnUnloaded(object sender, RoutedEventArgs args) => Disconnect();
        private void Connect()
        {
            if (!_connected)
            {
                _connected = true;
                _options.ColumnOrderChanged += OnOrderChanged;
                _options.PropertyChanged += OnOptionChanged;
                _grid.ColumnReordered += OnColumnReordered;
            }
            ApplyOrder();
            ApplyPathWidth();
            RequestFit();
        }
        private void Disconnect()
        {
            _connected = false;
            _options.ColumnOrderChanged -= OnOrderChanged;
            _options.PropertyChanged -= OnOptionChanged;
            _grid.ColumnReordered -= OnColumnReordered;
            _pendingFit?.Abort();
            _pendingFit = null;
        }
        private void OnOptionChanged(object? sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(OptionsViewModel.PathAutoWidth)) ApplyPathWidth();
        }
        private void ApplyPathWidth()
        {
            var column = _grid.Columns.FirstOrDefault(column => GetId(column) == "Path");
            if (column is null) return;
            // A long path can use its full width at the end of the table. When moved
            // near the front, the optional bounded mode leaves room for other columns.
            column.MaxWidth = _options.PathAutoWidth ? double.PositiveInfinity : 600;
            column.Width = _options.PathAutoWidth ? DataGridLength.Auto : new DataGridLength(320);
        }
        private void OnOrderChanged(object? sender, EventArgs args) => ApplyOrder();
        private void ApplyOrder()
        {
            if (_applying || _grid.Columns.Count == 0) return;

            // XAML may set FrozenColumnCount before adding any columns. WPF then
            // coerces it to zero and delays updating it until the first measure.
            // Refresh it before saving/restoring, or we permanently overwrite the
            // requested count with that temporary zero during initial ordering.
            _grid.CoerceValue(DataGrid.FrozenColumnCountProperty);
            var frozen = _grid.FrozenColumnCount;

            _applying = true;
            try
            {
                // Allow any column to cross the frozen boundary in the options dialog.
                _grid.FrozenColumnCount = 0;
                var columns = _grid.Columns.Where(column => GetId(column).Length != 0).ToDictionary(GetId);
                var index = 0;
                foreach (var option in _options.Columns)
                    if (columns.TryGetValue(option.Id, out var column)) column.DisplayIndex = index++;
            }
            finally { _grid.FrozenColumnCount = frozen; _applying = false; }
        }
        private void OnColumnReordered(object? sender, DataGridColumnEventArgs args)
        {
            if (_applying) return;
            _applying = true;
            try { _options.SetColumnOrder(_grid.Columns.OrderBy(column => column.DisplayIndex).Select(GetId)); }
            finally { _applying = false; }
        }
        public void RequestFit()
        {
            if (!_connected || _pendingFit?.Status == DispatcherOperationStatus.Pending) return;
            // Wait for translated/formatted cell bindings before measuring them.
            _pendingFit = _grid.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                _pendingFit = null;
                if (!_connected) return;
                foreach (var column in _grid.Columns)
                {
                    if (GetId(column) == "Path" && !_options.PathAutoWidth) continue;
                    column.Width = DataGridLength.SizeToHeader;
                    column.Width = DataGridLength.Auto;
                }
                _grid.InvalidateMeasure();
            }));
        }
        public void Dispose()
        {
            Disconnect();
            _grid.Loaded -= OnLoaded;
            _grid.Unloaded -= OnUnloaded;
        }
    }
}
