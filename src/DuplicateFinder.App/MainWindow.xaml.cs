using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using DuplicateFinder.Core;
using FileScope.App.Localization;
using FileScope.App.Infrastructure;
using System.Windows.Media;
using FileScope.App.Services;
using FileScope.Core.Models;

namespace DuplicateFinder.App;

public partial class MainWindow : Window
{
    private readonly ResultsViewModel _model;
    private readonly DuplicateScanner _scanner;
    private readonly CleanupService _cleanup;
    private readonly SettingsStore _store;
    private readonly IThemeService _theme;
    private Preferences _preferences;
    private IReadOnlyList<ScanIssue> _issues = [];
    private CancellationTokenSource? _job;
    private readonly LatestProgress _progress = new();
    private readonly DispatcherTimer _timer;
    private static LocalizationService Strings => LocalizationService.Instance;

    public MainWindow(ResultsViewModel model, DuplicateScanner scanner, CleanupService cleanup,
        Preferences preferences, SettingsStore store, IThemeService theme)
    {
        _model = model;
        _scanner = scanner;
        _cleanup = cleanup;
        _preferences = preferences;
        _store = store;
        _theme = theme;
        InitializeComponent();
        DataContext = model;
        ScanButton.Command = new AsyncRelayCommand(StartScanAsync, ShowError);
        CleanButton.Command = new AsyncRelayCommand(ChooseScopeAsync, ShowError);
        RecycleButton.Command = new AsyncRelayCommand(ManualRecycleAsync, ShowError);

        _timer = new(TimeSpan.FromMilliseconds(150), DispatcherPriority.Background, Tick, Dispatcher);
        _timer.Stop();
        model.PropertyChanged += ModelChanged;
        Closed += (_, _) => { _timer.Stop(); model.PropertyChanged -= ModelChanged; };
        ReloadFilters();
        UpdateButtons();
        Summary();
    }

    private void ReloadFilters()
    {
        KindBox.ItemsSource = new[] { Strings["All"], Strings["Duplicates"], Strings["Singles"], Strings["Diagnostics"] };
        SignatureBox.ItemsSource = new[] { Strings["AllSignatures"], Strings["ExceptValid"] }
            .Concat(Enum.GetValues<SignatureStatus>().Select(s => Strings["Signature_" + s])).ToArray();
        KindBox.SelectedIndex = 0;
        SignatureBox.SelectedIndex = 0;
    }

    private void FilterChanged(object sender, RoutedEventArgs e)
    {
        if (SignatureBox == null || KindBox.SelectedIndex < 0 || SignatureBox.SelectedIndex < 0) return;
        var index = SignatureBox.SelectedIndex;
        _model.Filter(FilterBox.Text, KindBox.SelectedIndex, index >= 2 ? (SignatureStatus)(index - 2) : null, index == 1);
        if (_job == null) Summary();
    }

    private void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ResultsViewModel.MarkedCount)) { UpdateButtons(); if (_job == null) Summary(); }
    }

    private void RowClick(object sender, MouseButtonEventArgs e)
    {
        if (_job != null || e.OriginalSource is not DependencyObject element) return;
        while (element is Visual and not DataGridRow)
        {
            // The actual checkbox supports keyboard and UI Automation through its two-way binding.
            if (element is CheckBox { DataContext: ResultRow checkedRow })
            {
                Files.SelectedItem = checkedRow;
                Files.CurrentCell = new DataGridCellInfo(checkedRow, Files.Columns[0]);
                return;
            }
            element = VisualTreeHelper.GetParent(element);
        }
        if (element is not DataGridRow { Item: ResultRow row }) return;
        if (e.ClickCount == 1) row.Marked = !row.Marked;
    }

    private void WindowKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.F1:
                ShowShortcuts(this, e);
                break;
            case Key.F5 when ScanButton.IsEnabled:
                ScanButton.Command.Execute(null);
                break;
            case Key.F when Keyboard.Modifiers == ModifierKeys.Control:
                FilterBox.Focus();
                FilterBox.SelectAll();
                break;
            case Key.Escape when _job != null:
                _job.Cancel();
                break;
            case Key.Space when _job == null && Keyboard.FocusedElement is DataGridCell { DataContext: ResultRow row }:
                row.Marked = !row.Marked;
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    private void OpenOptions(object sender, RoutedEventArgs e)
    {
        var dialog = new OptionsWindow(_preferences) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        var next = dialog.Result;
        try { _store.Save(next); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { MessageBox.Show(this, ex.Message, Strings["Options"]); return; }

        var rescan = !_preferences.Roots.SequenceEqual(next.Roots) || !_preferences.Hashes.SequenceEqual(next.Hashes)
            || _preferences.Embedded != next.Embedded || _preferences.Catalogs != next.Catalogs || _preferences.Online != next.Online
            || _preferences.SizeRange != next.SizeRange;
        var languageChanged = next.Language != _preferences.Language;
        _preferences = next;
        Strings.SetCulture(next.Language);
        Dispatcher.VerifyAccess();
        _theme.Apply(next.Theme);
        if (rescan) { _issues = []; _model.Replace([]); }
        if (languageChanged)
        {
            ReloadFilters();
            _model.Relocalize();
        }
        UpdateButtons();
        Summary();
    }

    private Task StartScanAsync(object? parameter)
    {
        if (_job != null || _preferences.Roots.Length == 0) return Task.CompletedTask;
        return RunJobAsync(async token =>
        {
            var scan = await Task.Run(() => _scanner.ScanAsync(_preferences.Roots, _preferences.Search, _progress, token), token);
            _issues = scan.Issues;
            _model.Replace(scan.Files);
        });
    }

    private async Task ChooseScopeAsync(object? parameter)
    {
        if (_job != null) return;
        var filtered = _model.Plan(true);
        var all = _model.Plan(false);
        var dialog = new ScopeWindow(filtered, all) { Owner = this };
        if (dialog.ShowDialog() == true) await RecycleAsync((dialog.Filtered ? filtered : all).Items);
    }

    private async Task ManualRecycleAsync(object? parameter)
    {
        if (_job != null) return;
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
            if (cancelled) Status.Text = Strings["Cancelled"] + " · " + Status.Text;
            Meter.IsIndeterminate = false;
            Meter.Value = 0;
        }
    }

    private void UpdateButtons()
    {
        var idle = _job == null;
        // Disable marking only. Fluent paints the entire grid white in its disabled state.
        _model.CanMark = idle;
        OptionsButton.IsEnabled = idle;
        ScanButton.IsEnabled = idle && _preferences.Roots.Length != 0;
        CancelButton.IsEnabled = !idle;
        CleanButton.IsEnabled = idle && _model.Rows.Count > 0;
        RecycleButton.IsEnabled = idle && _model.MarkedCount > 0;
        IssuesButton.IsEnabled = idle && _issues.Count > 0;
    }

    private void Summary()
    {
        Status.Text = _preferences.Roots.Length == 0 ? Strings["StartHint"] :
            Strings.Format("Summary", _model.View.Count, _model.Rows.Count, _model.MarkedCount, _issues.Count);
    }

    private void Tick(object? sender, EventArgs e)
    {
        if (_progress.Take() is not { } update) return;
        Status.Text = Strings["Stage_" + update.Stage] + " · " + update.Path;
        Meter.IsIndeterminate = update.Total == 0;
        if (update.Total > 0) { Meter.Maximum = update.Total; Meter.Value = update.Completed; }
    }

    private void ShowError(Exception ex) => MessageBox.Show(this, ex.Message, "DuplicateFinder", MessageBoxButton.OK, MessageBoxImage.Error);

    private void ShowIssues(object sender, RoutedEventArgs e) =>
        ShowOwnedDialog(new ReportWindow(Strings["Issues"], Strings["Diagnostics"], _issues.Select(i => $"{i.Path} · {i.Reason}").ToArray()));
    private void ShowAbout(object sender, RoutedEventArgs e) => ShowOwnedDialog(ReportWindow.About());
    private void ShowShortcuts(object sender, RoutedEventArgs e) => ShowOwnedDialog(new ShortcutsWindow());

    private void ShowOwnedDialog(Window window)
    {
        window.Owner = this;
        window.ShowDialog();
    }

    private void CancelJob(object sender, RoutedEventArgs e) => _job?.Cancel();
    private void WindowClosing(object? sender, CancelEventArgs e)
    {
        if (_job == null) return;
        e.Cancel = true;
        _job.Cancel();
    }

    private sealed class LatestProgress : IProgress<ScanProgress>
    {
        private ScanProgress? _latest;
        public void Report(ScanProgress value) => Interlocked.Exchange(ref _latest, value);
        public ScanProgress? Take() => Interlocked.Exchange(ref _latest, null);
    }
}
