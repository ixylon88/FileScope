using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DuplicateFinder.App.Services;
using DuplicateFinder.App.ViewModels;
using DuplicateFinder.Core.Models;
using DuplicateFinder.Core.Services;

namespace DuplicateFinder.App;

public partial class MainWindow : Window
{
    private readonly ResultsViewModel _model;
    private readonly DuplicateScanner _scanner;
    private readonly CleanupService _cleanup;
    private readonly SettingsStore _store;
    private readonly IThemeService _theme;
    private readonly WindowPlacement _placement;
    private readonly FileShellCommands<ResultRow> _fileActions;
    private Preferences _preferences;
    private IReadOnlyList<ScanIssue> _issues = [];
    private CancellationTokenSource? _job;
    private readonly LatestProgress _progress = new();
    private readonly DispatcherTimer _timer;
    private bool? _pathAutoWidth;

    private static LocalizationService Strings => LocalizationService.Instance;

    public AsyncRelayCommand ScanCommand => field ??= new(StartScanAsync, ShowError, _ => CanScan);
    public AsyncRelayCommand CleanCommand => field ??= new(ChooseScopeAsync, ShowError, _ => CanClean);
    public AsyncRelayCommand RecycleCommand => field ??= new(ManualRecycleAsync, ShowError, _ => CanRecycle);

    public AsyncRelayCommand ShowFilePropertiesCommand => _fileActions.Properties;
    public AsyncRelayCommand ShowInExplorerCommand => _fileActions.Explorer;

    public RelayCommand OptionsCommand => field ??= new(OpenOptions, () => _job is null);
    public RelayCommand CancelCommand => field ??= new(() => _job?.Cancel(), () => _job is not null);
    public RelayCommand IssuesCommand => field ??= new(ShowIssues, () => _job is null && _issues.Count > 0);
    public RelayCommand AboutCommand => field ??= new(OpenAbout);

    private bool CanScan => _job is null && _preferences.Roots.Length > 0;
    private bool CanClean => _job is null && _model.Rows.Count > 0;
    private bool CanRecycle => _job is null && _model.MarkedCount > 0;

    public MainWindow(ResultsViewModel model, DuplicateScanner scanner, CleanupService cleanup,
        Preferences preferences, SettingsStore store, IThemeService theme, IFileShellService fileShell)
    {
        _model = model;
        _scanner = scanner;
        _cleanup = cleanup;
        _preferences = preferences;
        _store = store;
        _theme = theme;

        _fileActions = new(fileShell, SelectedFileForShell, row => row.File.Path,
            row => IsLoaded && _model.Rows.Contains(row), RefreshFileActionError, ShowError);

        InitializeComponent();
        // A context menu has its own visual tree. Its commands belong to this window.
        ((FileRowContextMenu)Resources["FileMenu"]).DataContext = this;
        ShortcutsDialog.BindHelp(this, CreateShortcutsDialog);

        _placement = new WindowPlacement(this, preferences.MainWindow, centerOnWorkArea: true);
        _timer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromMilliseconds(150) };
        _timer.Tick += Tick;

        InitializeResultsView();
    }

    private void InitializeResultsView()
    {
        DataContext = _model;
        _model.SetHashFormat(_preferences.HashFormat);
        SetPathWidth(_preferences.PathAutoWidth);
        _model.PropertyChanged += ModelChanged;

        UpdateButtons();
        Summary();
    }

    private void ModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ResultsViewModel.MarkedCount)) UpdateButtons();
        if (_job is null && e.PropertyName is nameof(ResultsViewModel.MarkedCount) or nameof(ResultsViewModel.VisibleCount))
            Summary();

        if (e.PropertyName == nameof(ResultsViewModel.View)) _fileActions.ClearError();
    }

    private ResultRow? SelectedFileForShell() =>
        _job == null && IsLoaded && Files.SelectedItems.Count == 1 ? Files.SelectedItem as ResultRow : null;

    private void FileSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (ReferenceEquals(args.OriginalSource, Files)) _fileActions.NotifyCanExecuteChanged();
    }

    private void FileMenuOpening(object sender, ContextMenuEventArgs args) =>
        FileRowContextMenu.SelectTargetRow(Files, (DataGridRow)sender);

    private void RefreshFileActionError()
    {
        FileActionErrorText.Text = _fileActions.GetErrorText(Strings);
        FileActionErrorText.Visibility = _fileActions.HasError ? Visibility.Visible : Visibility.Collapsed;
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

    private void OpenAbout()
    {
        var dialog = WindowPlacement.RememberDialogSize(AboutWindow.Create(height: 750),
            _preferences.AboutWindow, value => SavePlacement(_preferences with { AboutWindow = value }));

        ShowOwnedDialog(dialog);
    }

    private ShortcutsDialog CreateShortcutsDialog() =>
        WindowPlacement.RememberDialogSize(ShortcutsWindow.Create(height: 810),
            _preferences.ShortcutsWindow, value => SavePlacement(_preferences with { ShortcutsWindow = value }));

    private void OpenOptions()
    {
        Dispatcher.VerifyAccess();
        var dialog = new OptionsWindow(_preferences) { Owner = this };
        dialog.PreviewChanged += ApplyAppearance;
        dialog.Saving += _store.Save;

        if (dialog.ShowDialog() != true)
        {
            ApplyAppearance(_preferences);
            if (dialog.ChangedPlacement is { } placement)
                SavePlacement(_preferences with { OptionsWindow = placement });
            return;
        }

        var next = dialog.Result;
        var rescan = !_preferences.Roots.SequenceEqual(next.Roots) || !_preferences.Hashes.SequenceEqual(next.Hashes)
            || _preferences.Embedded != next.Embedded || _preferences.Catalogs != next.Catalogs || _preferences.Online != next.Online
            || _preferences.SizeRange != next.SizeRange;
        _preferences = next;
        ApplyAppearance(next);
        if (rescan) { _issues = []; _model.Replace([]); }
        UpdateButtons();
        Summary();
    }

    private void SavePlacement(Preferences next)
    {
        if (TrySave(next)) _preferences = next;
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
            _model.Relocalize();
            RefreshFileActionError();
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
        PathColumnWidth.Apply(PathColumn, automatic);
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
        _model.IsBusy = _job != null;
        ScanCommand.NotifyCanExecuteChanged();
        CleanCommand.NotifyCanExecuteChanged();
        RecycleCommand.NotifyCanExecuteChanged();
        _fileActions.NotifyCanExecuteChanged();
        OptionsCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        IssuesCommand.NotifyCanExecuteChanged();
    }

    private void Summary()
    {
        Status.Text = _preferences.Roots.Length == 0 ? Strings["StartHint"] :
            Strings.Format("Summary", _model.VisibleCount, _model.Rows.Count, _model.MarkedCount, _issues.Count);
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

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        _model.PropertyChanged -= ModelChanged;

        base.OnClosed(e);
    }

    private sealed class LatestProgress : IProgress<ScanProgress>
    {
        private ScanProgress? _latest;
        public void Report(ScanProgress value) => Interlocked.Exchange(ref _latest, value);
        public ScanProgress? Take() => Interlocked.Exchange(ref _latest, null);
    }
}
