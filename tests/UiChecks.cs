using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using FileScope.App;
using FileScope.App.Localization;
using FileScope.App.Services;
using FileScope.App.ViewModels;
using FileScope.Core.Abstractions;
using FileScope.Core.Models;

internal static class UiChecks
{
    private static int _passed;
    [STAThread]
    private static int Main()
    {
        Application? app = null;
        MainWindow? window = null;
        var originalContext = SynchronizationContext.Current;
        try
        {
            // App.OnStartup creates the production window when the dispatcher is
            // pumped. Use a plain host and load the resources the tested view needs.
            app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("/FileScope.App;component/Styles/ThemePalette.xaml", UriKind.Relative)
            });
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("/FileScope.App;component/Styles/Controls.xaml", UriKind.Relative)
            });
            app.Resources.Add("BoolToVisibility", new BooleanToVisibilityConverter());
            // Let the same service as production load/switch the Fluent dictionaries.
            var themes = new ThemeService(app);
            // Commands are invoked before PumpUntil enters its dispatcher frame.
            // Their await continuations must already capture the UI context here.
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            var strings = LocalizationService.Instance;
            CheckFileIntake(strings);
            var service = new ImmediateAnalysis();
            using var vm = new MainViewModel(service, new EmptyPicker(), strings, themes);
            window = new MainWindow(vm);
            // Initialize and lay out real XAML, without opening a visible window.
            window.Measure(new Size(1440, 900));
            window.Arrange(new Rect(0, 0, 1440, 900));
            DrainDispatcher();
            Check(vm.Rows.Count == 0 && vm.ShowEmptyState, "initial empty state");
            foreach (var language in vm.Languages)
            {
                vm.SelectedLanguage = language;
                DrainDispatcher();
                Check(window.Title == strings["WindowTitle"] && !window.Title.StartsWith('['),
                    $"XAML resource binding / {language.CultureName}");
                Check(vm.Themes[0].Label == strings["Theme_Light"] && vm.Themes[1].Label == strings["Theme_Dark"]
                    && !vm.Themes[0].Label.StartsWith('['), $"theme labels / {language.CultureName}");
            }
            vm.SelectedLanguage = vm.Languages.Single(l => l.CultureName == "en");
            var first = Path.Combine(Path.GetTempPath(), "FileScope-a.bin");
            var second = Path.Combine(Path.GetTempPath(), "FileScope-b.bin");
            vm.DropFilesCommand.Execute(new[] { first, first, second });
            PumpUntil(() => !vm.IsBusy && vm.Rows.Count == 2);
            Check(vm.Rows.All(r => r.State == RowStatus.Completed), "drop command processes all unique paths");
            Check(vm.Rows.All(r => r.SHA256 == "CHECK-SHA256"), "results reach the grid model");
            Check(vm.Rows.All(r => r.MD5.Length == 0), "unselected hashes remain empty");
            CheckPresentation(window, vm, themes);
            vm.SearchText = "check-sha256";
            Check(vm.RowsView.Count == 2, "case-insensitive checksum search");
            vm.SearchText = "does-not-exist";
            Check(vm.RowsView.Count == 0 && vm.ShowNoMatches, "no-match filter state");
            vm.ResetFiltersCommand.Execute(null);
            vm.SignatureFilterOption = vm.SignatureFilters.Single(x => x.Value == SignatureFilter.Valid);
            Check(vm.RowsView.Count == 0, "signature filter excludes unsigned files");
            vm.SignatureFilterOption = vm.SignatureFilters.Single(x => x.Value == SignatureFilter.NoSignature);
            Check(vm.RowsView.Count == 2, "signature filter matches unsigned files");
            vm.ResetFiltersCommand.Execute(null);
            vm.StateFilter = vm.StateFilters.Single(x => x.Value == RowStatus.Completed);
            Check(vm.RowsView.Count == 2, "analysis-state filter");
            vm.SelectedLanguage = vm.Languages.Single(x => x.CultureName == "de");
            Check(vm.Rows[0].StateText == "Fertig", "existing row status changes language");
            Check(vm.Rows[0].SignatureText == strings["Signature_NoEmbeddedSignature"], "existing signature changes language");
            vm.SelectedLanguage = vm.Languages.Single(x => x.CultureName == "zh-Hans");
            Check(vm.Rows[0].StateText == "已完成", "Chinese satellite resource is loaded");
            vm.ChecksumOptions.Single(x => x.Algorithm == ChecksumAlgorithm.SHA512).IsSelected = true;
            vm.ReanalyseCommand.Execute(null);
            PumpUntil(() => !vm.IsBusy && vm.Rows.All(r => r.SHA512 == "CHECK-SHA512"));
            Check(service.LastOptions!.Algorithms.Contains(ChecksumAlgorithm.SHA512), "reanalysis uses new options");
            vm.ClearCommand.Execute(null);
            Check(vm.Rows.Count == 0 && vm.RowsView.Count == 0 && vm.SearchText.Length == 0,
                "clear removes results and resets filters");

            var blocked = new BlockingAnalysis();
            using var busyVm = new MainViewModel(blocked, new EmptyPicker(), strings, new TestThemeService());
            busyVm.DropFilesCommand.Execute(new[] { first });
            PumpUntil(() => blocked.Started.Task.IsCompleted);
            Check(busyVm.IsBusy && !busyVm.DropFilesCommand.CanExecute(new[] { second }),
                "concurrent drop is disabled during a batch");
            busyVm.ClearCommand.Execute(null);
            Check(blocked.Token.IsCancellationRequested, "clear requests cancellation");
            Check(busyVm.IsBusy, "clear waits for a non-interruptible strategy");
            blocked.Release.TrySetResult();
            PumpUntil(() => !busyVm.IsBusy);
            Check(busyVm.Rows.Count == 0 && busyVm.ShowEmptyState, "clear empties the grid after the worker finishes");
            blocked.Progress?.Report(new AnalysisProgress(AnalysisStage.Reading, 1, 1));
            DrainDispatcher();
            Check(busyVm.Rows.Count == 0 && busyVm.Progress == 0 && busyVm.CurrentFile.Length == 0,
                "late progress cannot restore cleared state");

            var cancelled = new BlockingAnalysis();
            using var cancelVm = new MainViewModel(cancelled, new EmptyPicker(), strings, new TestThemeService());
            cancelVm.DropFilesCommand.Execute(new[] { first, second });
            PumpUntil(() => cancelled.Started.Task.IsCompleted);
            cancelVm.CancelCommand.Execute(null);
            cancelled.Release.TrySetResult();
            PumpUntil(() => !cancelVm.IsBusy);
            Check(cancelVm.Rows.Count == 2, "cancel keeps rows");
            Check(cancelVm.Rows.Any(r => r.State == RowStatus.Cancelled), "unprocessed rows are marked cancelled");
            Check(cancelVm.ReanalyseCommand.CanExecute(null), "cancelled batch can be retried");
            Console.WriteLine($"PASS: {_passed} UI / MVVM assertions.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally
        {
            window?.Close();
            app?.Shutdown();
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }
    }

    private static void CheckFileIntake(LocalizationService strings)
    {
        var picker = new TestPicker();
        using var vm = new MainViewModel(new ImmediateAnalysis(), picker, strings, new TestThemeService());
        var first = Path.Combine(Path.GetTempPath(), "FileScope-intake-a.bin");
        var second = Path.Combine(Path.GetTempPath(), "FileScope-intake-b.bin");
        var third = Path.Combine(Path.GetTempPath(), "FileScope-intake-c.bin");
        var fourth = Path.Combine(Path.GetTempPath(), "FileScope-intake-d.bin");
        var observedAdds = 0;
        NotifyCollectionChangedEventHandler readView = (sender, change) =>
        {
            // Model a bound control reading the view synchronously when an item
            // arrives. These public reads must remain valid during notifications.
            _ = vm.RowsView.Count;
            _ = vm.RowsView.CurrentPosition;
            if (change.Action == NotifyCollectionChangedAction.Add) observedAdds++;
        };
        var viewNotifications = (INotifyCollectionChanged)vm.RowsView;
        viewNotifications.CollectionChanged += readView;
        try
        {
            void AwaitFinished(int count)
            {
                PumpUntil(() => vm.HasError || (!vm.IsBusy && vm.Rows.Count == count
                    && vm.Rows.All(r => r.State == RowStatus.Completed)));
                Check(!vm.HasError, "file intake completes without a view error: " + vm.LastError);
            }

            vm.DropFilesCommand.Execute(new[] { first });
            AwaitFinished(1);
            Check(vm.RowsView.Count == 1 && observedAdds == 1,
                "single-file drop publishes a readable view");

            vm.DropFilesCommand.Execute(new[] { first, second, second });
            AwaitFinished(2);
            Check(vm.RowsView.Count == 2 && observedAdds == 2,
                "multi-file drop skips repeated paths without losing new files");

            vm.RowsView.MoveCurrentTo(vm.Rows[0]);
            vm.SearchText = "FileScope-intake";
            vm.RowsView.SortDescriptions.Add(new SortDescription(
                nameof(FileRowViewModel.FileName), ListSortDirection.Descending));
            picker.Files = [second, third];
            vm.BrowseCommand.Execute(null);
            AwaitFinished(3);
            Check(vm.RowsView.Count == 3
                && ((FileRowViewModel)vm.RowsView.GetItemAt(0)).FullPath == third,
                "file picker adds files to a filtered, sorted view");

            vm.SearchText = "no-file-matches-this-filter";
            vm.DropFilesCommand.Execute(new[] { fourth });
            AwaitFinished(4);
            Check(vm.RowsView.Count == 0 && vm.Rows.Single(r => r.FullPath == fourth).SHA256 == "CHECK-SHA256",
                "files hidden by the current filter are still analysed");
            vm.ResetFiltersCommand.Execute(null);
            Check(vm.RowsView.Count == 4, "resetting filters reveals all processed files");

            vm.ClearCommand.Execute(null);
            vm.DropFilesCommand.Execute(new[] { first });
            AwaitFinished(1);
            Check(vm.RowsView.Count == 1, "the same file can be dropped again after clear");
        }
        finally { viewNotifications.CollectionChanged -= readView; }
    }

    private static void CheckPresentation(MainWindow window, MainViewModel vm, ThemeService themes)
    {
        var grid = (DataGrid)window.FindName("ResultsGrid");
        var selector = (ComboBox)window.FindName("ThemeSelector");
        var columns = grid.Columns.OfType<DataGridTextColumn>()
            .ToDictionary(column => ((Binding)column.Binding).Path.Path);
        var results = vm.Rows.Select(row => row.Result).ToArray();
        string[] certificateColumns = ["SignatureText", "Publisher", "Subject", "Issuer", "SerialNumber",
            "ValidFrom", "ValidUntil", "CertificateSha1", "CertificateSha256", "CertificateAlgorithm", "NativeCode"];

        foreach (var option in vm.ChecksumOptions)
        {
            var column = columns[option.Algorithm.ToString()];
            Check(column.Visibility == (option.IsSelected ? Visibility.Visible : Visibility.Collapsed),
                $"initial column visibility / {option.Label}");
            foreach (var selected in new[] { true, false, true })
            {
                option.IsSelected = selected;
                DrainDispatcher();
                Check(column.Visibility == (selected ? Visibility.Visible : Visibility.Collapsed),
                    $"bound column responds to checkbox / {option.Label} / {selected}");
            }
        }
        foreach (var enabled in new[] { false, true, false })
        {
            vm.InspectCertificates = enabled;
            DrainDispatcher();
            Check(certificateColumns.All(path => columns[path].Visibility ==
                (enabled ? Visibility.Visible : Visibility.Collapsed)), "certificate columns toggle together");
            Check(vm.CanChooseOnlineRevocation == enabled, "revocation enablement follows certificate option");
            Check(new[] { "FileName", "SizeText", "ModifiedText", "FullPath", "Diagnostic" }
                .All(path => columns[path].Visibility == Visibility.Visible), "file metadata and diagnostics stay visible");
        }
        foreach (var option in vm.ChecksumOptions) option.IsSelected = option.Algorithm == ChecksumAlgorithm.SHA256;
        vm.InspectCertificates = true;

        void LayoutGrid()
        {
            grid.ApplyTemplate();
            grid.Measure(new Size(1280, 480));
            grid.Arrange(new Rect(0, 0, 1280, 480));
            grid.UpdateLayout();
            DrainDispatcher();
        }
        void CheckRowColor(int index, string brushKey)
        {
            var row = (DataGridRow?)grid.ItemContainerGenerator.ContainerFromIndex(index);
            Check(row is not null, $"row {index} is realized for visual checks");
            var surface = (Border)row!.Template.FindName("RowSurface", row);
            Check(surface.Background is SolidColorBrush actual
                && actual.Color == ((SolidColorBrush)grid.FindResource(brushKey)).Color,
                $"rendered row {index} uses {brushKey} / {themes.CurrentTheme}");
        }

        var brushBeforeSwitch = window.Background;
        foreach (var theme in new[] { AppTheme.Dark, AppTheme.Light, AppTheme.Dark, AppTheme.Light })
        {
            selector.SelectedIndex = vm.Themes.ToList().FindIndex(option => option.Value == theme);
            DrainDispatcher();
            Check(themes.CurrentTheme == theme && vm.SelectedTheme.Value == theme,
                "theme selector invokes the injected service");
#pragma warning disable WPF0001
            Check(Application.Current.ThemeMode == (theme == AppTheme.Dark ? ThemeMode.Dark : ThemeMode.Light),
                "native Fluent theme matches palette");
#pragma warning restore WPF0001
            Check(!ReferenceEquals(window.Background, brushBeforeSwitch), "window updates its dynamic palette");
            brushBeforeSwitch = window.Background;
            grid.UnselectAll();
            LayoutGrid();
            CheckRowColor(0, "RowBrush");
            CheckRowColor(1, "AlternateRowBrush");
            Check(((SolidColorBrush)grid.RowBackground).Color != ((SolidColorBrush)grid.AlternatingRowBackground).Color,
                "alternating rows have different colors");
            grid.SelectedItem = vm.RowsView.GetItemAt(1);
            DrainDispatcher();
            CheckRowColor(1, "SelectionBrush");
            Check(vm.Rows.Select(row => row.Result).Zip(results).All(pair => ReferenceEquals(pair.First, pair.Second)),
                "presentation changes retain analysis results");
            Check(columns["MD5"].Visibility == Visibility.Collapsed && columns["SHA256"].Visibility == Visibility.Visible,
                "theme switch preserves column choices");
        }

        grid.UnselectAll();
        vm.RowsView.SortDescriptions.Add(new SortDescription(nameof(FileRowViewModel.FileName), ListSortDirection.Descending));
        LayoutGrid();
        CheckRowColor(0, "RowBrush");
        CheckRowColor(1, "AlternateRowBrush");
        vm.SearchText = "FileScope-a.bin";
        LayoutGrid();
        Check(grid.Items.Count == 1, "filter removes nonmatching visual rows");
        CheckRowColor(0, "RowBrush");
        vm.RowsView.SortDescriptions.Clear();
        vm.ResetFiltersCommand.Execute(null);
        LayoutGrid();
    }

    private static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception("FAIL: " + label);
        _passed++;
        Console.WriteLine("PASS: " + label);
    }
    private static void DrainDispatcher()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
    private static void PumpUntil(Func<bool> condition)
    {
        if (condition()) { DrainDispatcher(); return; }
        var clock = Stopwatch.StartNew();
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) => { if (condition() || clock.Elapsed > TimeSpan.FromSeconds(10)) frame.Continue = false; };
        timer.Start();
        try { Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
        if (!condition()) throw new TimeoutException("UI check timed out.");
        DrainDispatcher();
    }

    private sealed class EmptyPicker : IFilePickerService
    {
        public string[] PickFiles(string title, string allFilesLabel) => [];
    }
    private sealed class TestThemeService : IThemeService
    {
        public AppTheme CurrentTheme { get; private set; } = AppTheme.Light;
        public void Apply(AppTheme theme) => CurrentTheme = theme;
    }
    private sealed class TestPicker : IFilePickerService
    {
        public string[] Files { get; set; } = [];
        public string[] PickFiles(string title, string allFilesLabel) => Files;
    }
    private sealed class ImmediateAnalysis : IFileAnalysisService
    {
        public AnalysisOptions? LastOptions { get; private set; }
        public Task<FileAnalysisResult> AnalyzeAsync(string path, AnalysisOptions options,
            IProgress<AnalysisProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            LastOptions = options;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Result(path, options));
        }
    }
    private sealed class BlockingAnalysis : IFileAnalysisService
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken Token;
        public IProgress<AnalysisProgress>? Progress;
        public async Task<FileAnalysisResult> AnalyzeAsync(string path, AnalysisOptions options,
            IProgress<AnalysisProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            Token = cancellationToken;
            Progress = progress;
            Started.TrySetResult();
            // Models a synchronous OS operation that cannot observe cancellation yet.
            await Release.Task;
            cancellationToken.ThrowIfCancellationRequested();
            return Result(path, options);
        }
    }
    private static FileAnalysisResult Result(string path, AnalysisOptions options) => new(
        path, 3, DateTimeOffset.UtcNow,
        options.Algorithms.ToDictionary(a => a, a => "CHECK-" + a),
        new SignatureResult(SignatureStatus.NoEmbeddedSignature, options.RevocationMode));
}
