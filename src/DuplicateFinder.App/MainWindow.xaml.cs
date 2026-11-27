using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using DuplicateFinder.Core;
using System.Windows.Media;

namespace DuplicateFinder.App;

public partial class MainWindow : Window
{
    private readonly ResultsViewModel _model;
    private readonly DuplicateScanner _scanner;
    private readonly CleanupService _cleanup;
    private readonly SettingsStore _store;
    private readonly IThemeService _theme;
    private readonly WindowPlacement _placement;
    private Preferences _preferences;
    private IReadOnlyList<ScanIssue> _issues = [];
    private CancellationTokenSource? _job;
    private readonly LatestProgress _progress = new();
    private readonly DispatcherTimer _timer;
    private static LocalizationService Strings => LocalizationService.Instance;

    public AsyncRelayCommand ScanCommand { get; }
    public AsyncRelayCommand CleanCommand { get; }
    public AsyncRelayCommand RecycleCommand { get; }
    public RelayCommand OptionsCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand IssuesCommand { get; }
    public RelayCommand AboutCommand { get; }
    public RelayCommand ResetFiltersCommand { get; }

    public MainWindow(ResultsViewModel model, DuplicateScanner scanner, CleanupService cleanup,
        Preferences preferences, SettingsStore store, IThemeService theme)
    {
        _model = model;
        _scanner = scanner;
        _cleanup = cleanup;
        _preferences = preferences;
        _store = store;
        _theme = theme;
        ScanCommand = new(StartScanAsync, ShowError, _ => _job == null && _preferences.Roots.Length > 0);
        CleanCommand = new(ChooseScopeAsync, ShowError, _ => _job == null && _model.Rows.Count > 0);
        RecycleCommand = new(ManualRecycleAsync, ShowError, _ => _job == null && _model.MarkedCount > 0);
        OptionsCommand = new(OpenOptions, () => _job == null);
        CancelCommand = new(() => _job?.Cancel(), () => _job != null);
        IssuesCommand = new(ShowIssues, () => _job == null && _issues.Count > 0);
        AboutCommand = new(() => ShowOwnedDialog(AboutWindow.Create()));
        ResetFiltersCommand = new(ResetFilters);

        InitializeComponent();
        ShortcutsDialog.BindHelp(this, () => ShortcutsWindow.Create());
        DataContext = model;
        _placement = new WindowPlacement(this, preferences.MainWindow, centerOnWorkArea: true);
        model.SetHashFormat(preferences.HashFormat);
        SetPathWidth(preferences.PathAutoWidth);

        _timer = new(TimeSpan.FromMilliseconds(150), DispatcherPriority.Background, Tick, Dispatcher);
        _timer.Stop();
        model.PropertyChanged += ModelChanged;
        Closed += (_, _) => { _timer.Stop(); model.PropertyChanged -= ModelChanged; };
        ReloadFilters();
        UpdateButtons();
        Summary();
    }

    private bool _settingFilters;
    private bool? _pathAutoWidth;

    private void ReloadFilters()
    {
        var kind = Math.Max(0, KindBox.SelectedIndex);
        var signature = SignatureBox.SelectedValue is SignatureFilter selected ? selected : SignatureFilter.All;
        _settingFilters = true;
        try
        {
            KindBox.ItemsSource = new[] { Strings["AllFiles"], Strings["Duplicates"], Strings["Singles"], Strings["Diagnostics"] };
            KindBox.SelectedIndex = kind;
            SignatureBox.ItemsSource = SignatureFilter.Options(Strings);
            SignatureBox.SelectedValue = signature;
        }
        finally { _settingFilters = false; }
        ApplyFilters();
    }

    private void ResetFilters()
    {
        _settingFilters = true;
        try
        {
            FilterBox.Clear();
            KindBox.SelectedIndex = 0;
            SignatureBox.SelectedValue = SignatureFilter.All;
        }
        finally { _settingFilters = false; }
        ApplyFilters();
    }

    private void FilterChanged(object sender, RoutedEventArgs e) => ApplyFilters();

    private void ApplyFilters()
    {
        if (_settingFilters || SignatureBox?.SelectedValue is not SignatureFilter filter || KindBox.SelectedIndex < 0) return;
        _model.Filter(FilterBox.Text, KindBox.SelectedIndex, filter);
        if (_job == null) Summary();
    }

    private void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ResultsViewModel.MarkedCount)) { UpdateButtons(); if (_job == null) Summary(); }
    }

    private void RowClick(object sender, MouseButtonEventArgs e)
    {
        if (_job != null || sender is not DataGridRow { Item: ResultRow row } || e.OriginalSource is not DependencyObject element) return;

        while (element is Visual && element != sender)
        {
            // The checkbox handles its own toggle. Select its row for the details pane.
            if (element is CheckBox)
            {
                Files.SelectedItem = row;
                Files.CurrentCell = new DataGridCellInfo(row, Files.Columns[0]);
                return;
            }
            element = VisualTreeHelper.GetParent(element);
        }

        // Modified row clicks belong to the grid's range and multiple selection.
        if (e.ClickCount == 1 && Keyboard.Modifiers == ModifierKeys.None) row.Marked = !row.Marked;
    }

    private void WindowKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.F5 when ScanCommand.CanExecute(null):
                ScanCommand.Execute(null);
                break;
            case Key.F when Keyboard.Modifiers == ModifierKeys.Control:
                FilterBox.Focus();
                FilterBox.SelectAll();
                break;
            case Key.Escape when CancelCommand.CanExecute(null):
                CancelCommand.Execute(null);
                break;
            case Key.Space when _job == null && Keyboard.Modifiers == ModifierKeys.None
                && Keyboard.FocusedElement is DataGridCell { DataContext: ResultRow row }:
                row.Marked = !row.Marked;
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    private void OpenOptions()
    {
        Dispatcher.VerifyAccess();
        var dialog = new OptionsWindow(_preferences) { Owner = this };
        dialog.PreviewChanged += ApplyAppearance;
        var accepted = dialog.ShowDialog() == true;

        var next = (accepted ? dialog.Result : _preferences) with { OptionsWindow = dialog.SavedPlacement };
        if (!TrySave(next))
        {
            ApplyAppearance(_preferences);
            return;
        }

        var rescan = !_preferences.Roots.SequenceEqual(next.Roots) || !_preferences.Hashes.SequenceEqual(next.Hashes)
            || _preferences.Embedded != next.Embedded || _preferences.Catalogs != next.Catalogs || _preferences.Online != next.Online
            || _preferences.SizeRange != next.SizeRange;
        _preferences = next;
        ApplyAppearance(next);
        if (rescan) { _issues = []; _model.Replace([]); }
        UpdateButtons();
        Summary();
    }

    private bool TrySave(Preferences preferences)
    {
        try { _store.Save(preferences); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message, Strings["Options"]);
            return false;
        }
    }

    private void ApplyAppearance(Preferences next)
    {
        if (Strings.Culture.Name != next.Language)
        {
            Strings.SetCulture(next.Language);
            ReloadFilters();
            _model.Relocalize();
            Summary();
        }

        Dispatcher.VerifyAccess();
        _theme.Apply(next.Theme);
        _model.SetHashFormat(next.HashFormat);
        SetPathWidth(next.PathAutoWidth);
    }

    private void SetPathWidth(bool automatic)
    {
        if (_pathAutoWidth == automatic) return;
        _pathAutoWidth = automatic;
        PathColumn.MaxWidth = automatic ? double.PositiveInfinity : 600;
        PathColumn.Width = automatic ? DataGridLength.Auto : new DataGridLength(320);
    }

    private Task StartScanAsync(object? parameter)
    {
        return RunJobAsync(async token =>
        {
            var scan = await Task.Run(() => _scanner.ScanAsync(_preferences.Roots, _preferences.Search, _progress, token), token);
            _issues = scan.Issues;
            _model.Replace(scan.Files);
        });
    }

    private async Task ChooseScopeAsync(object? parameter)
    {
        var filtered = _model.Plan(true);
        var all = _model.Plan(false);
        var dialog = new ScopeWindow(filtered, all) { Owner = this };
        if (dialog.ShowDialog() == true) await RecycleAsync((dialog.Filtered ? filtered : all).Items);
    }

    private async Task ManualRecycleAsync(object? parameter)
    {
        var items = _model.Manual();
        if (items.Count == 0) return;
        var last = _model.LastCopiesSelected();
        var intro = Strings.Format("ManualPrompt", items.Count);
        if (last != 0) intro += Environment.NewLine + Strings.Format("LastCopy", last);
        var dialog = new ReportWindow(Strings["Recycle"], intro, items.Select(i => i.File.Path).ToArray(), confirm: true) { Owner = this };
        if (dialog.ShowDialog() == true) await RecycleAsync(items);
    }

    private async Task RecycleAsync(IReadOnlyList<CleanupItem> items)
    {
        IReadOnlyList<RecycleResult> result = [];
        await RunJobAsync(async token =>
        {
            // The shell transfer provider lives on one STA for the entire operation.
            var completion = new TaskCompletionSource<IReadOnlyList<RecycleResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                try { completion.SetResult(_cleanup.Execute(items, _progress, token)); }
                catch (Exception ex) { completion.SetException(ex); }
            })
            { IsBackground = true };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            result = await completion.Task;
            _model.RemoveMoved(result);
        });

        var report = result.Select(r => $"{Strings[r.Moved ? "Moved" : "Skipped"]} · {r.File.Path}  {r.Reason}").ToList();
        if (result.Count < items.Count) report.Add(Strings.Format("Unprocessed", items.Count - result.Count));
        ShowOwnedDialog(new ReportWindow(Strings["Recycle"], Strings.Format("RecycledCount", result.Count(r => r.Moved), items.Count), report));
    }

    internal async Task RunJobAsync(Func<CancellationToken, Task> work)
    {
        if (_job != null) return;
        using var job = new CancellationTokenSource();
        _job = job;
        UpdateButtons();
        _timer.Start();
        var cancelled = false;
        try { await work(job.Token); cancelled = job.IsCancellationRequested; }
        catch (OperationCanceledException) when (job.IsCancellationRequested) { cancelled = true; }
        catch (Exception ex) { ShowError(ex); }
        finally
        {
            _timer.Stop();
            _progress.Take();
            _job = null;
            UpdateButtons();
            Summary();
            if (cancelled) Status.Text = Strings["Row_Cancelled"] + " · " + Status.Text;
            Meter.IsIndeterminate = false;
            Meter.Value = 0;
        }
    }

    private void UpdateButtons()
    {
        // Keep the grid readable while a job is running. Only marking and the affected commands are disabled.
        _model.CanMark = _job == null;
        ScanCommand.NotifyCanExecuteChanged();
        CleanCommand.NotifyCanExecuteChanged();
        RecycleCommand.NotifyCanExecuteChanged();
        OptionsCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        IssuesCommand.NotifyCanExecuteChanged();
    }

    private void Summary()
    {
        Status.Text = _preferences.Roots.Length == 0 ? Strings["StartHint"] :
            Strings.Format("Summary", _model.View.Count, _model.Rows.Count, _model.MarkedCount, _issues.Count);
    }

    private void Tick(object? sender, EventArgs e)
    {
        if (_progress.Take() is not { } update) return;
        var stage = update.Stage == "Search" ? Strings.Format("Stage_Search", update.Completed) : Strings["Stage_" + update.Stage];
        Status.Text = stage + " · " + update.Path;
        Meter.IsIndeterminate = update.Total == 0;
        if (update.Total > 0) { Meter.Maximum = update.Total; Meter.Value = update.Completed; }
    }

    private void ShowError(Exception ex) => MessageBox.Show(this, ex.Message, "DuplicateFinder", MessageBoxButton.OK, MessageBoxImage.Error);

    private void ShowIssues() =>
        ShowOwnedDialog(new ReportWindow(Strings["Issues"], Strings["Diagnostics"], _issues.Select(i => $"{i.Path} · {i.Reason}").ToArray()));
    private void ShowOwnedDialog(Window window)
    {
        window.Owner = this;
        window.ShowDialog();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_job != null)
        {
            e.Cancel = true;
            _job.Cancel();
            return;
        }

        if (!e.Cancel) TrySave(_preferences with { MainWindow = _placement.Capture() });
    }

    private sealed class LatestProgress : IProgress<ScanProgress>
    {
        private ScanProgress? _latest;
        public void Report(ScanProgress value) => Interlocked.Exchange(ref _latest, value);
        public ScanProgress? Take() => Interlocked.Exchange(ref _latest, null);
    }
}
