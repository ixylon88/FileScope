using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using FileScope.App.Behaviors;
using FileScope.App.Presentation;
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
                Source = new Uri("/PresentationFramework.Fluent;component/Themes/Fluent.Light.xaml", UriKind.Relative)
            });
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("/FileScope.App;component/Styles/Controls.xaml", UriKind.Relative)
            });
            app.Resources.Add("BoolToVisibility", new BooleanToVisibilityConverter());
            // Commands are invoked before PumpUntil enters its dispatcher frame.
            // Their await continuations must already capture the UI context here.
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            var strings = LocalizationService.Instance;
            CheckCheckboxPersistence(strings);
            CheckFileIntake(strings);
            CheckMultipleSignatures(strings);
            CheckWindowPersistence(strings);
            CheckHorizontalScrolling(strings);
            var service = new ImmediateAnalysis();
            using var vm = new MainViewModel(service, new EmptyPicker(), strings, CreateOptions(strings));
            window = new MainWindow(vm);
            // Initialize and lay out real XAML, without opening a visible window.
            window.Measure(new Size(1440, 900));
            window.Arrange(new Rect(0, 0, 1440, 900));
            DrainDispatcher();
            Check(vm.Rows.Count == 0 && vm.ShowEmptyState, "initial empty state");
            foreach (var language in vm.Options.Languages)
            {
                vm.Options.SelectedLanguage = language;
                DrainDispatcher();
                Check(window.Title == strings["WindowTitle"] && !window.Title.StartsWith('['),
                    $"XAML resource binding / {language.CultureName}");
                Check(vm.Options.HashFormats.All(option => !option.Label.StartsWith('[')),
                    $"hash format labels / {language.CultureName}");
            }
            vm.Options.SelectedLanguage = vm.Options.Languages.Single(l => l.CultureName == "en");
            var first = Path.Combine(Path.GetTempPath(), "FileScope-a.bin");
            var second = Path.Combine(Path.GetTempPath(), "FileScope-b.bin");
            vm.DropFilesCommand.Execute(new[] { first, first, second });
            PumpUntil(() => !vm.IsBusy && vm.Rows.Count == 2);
            Check(vm.Rows.All(r => r.State == RowStatus.Completed), "drop command processes all unique paths");
            Check(vm.Rows.All(r => r.SHA256 == HashValue(ChecksumAlgorithm.SHA256)), "results reach the grid model");
            Check(vm.Rows.All(r => r.MD5.Length == 0), "unselected hashes remain empty");
            CheckPresentation(window, vm, service);
            vm.SearchText = HashValue(ChecksumAlgorithm.SHA256).ToLowerInvariant();
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
            vm.Options.SelectedLanguage = vm.Options.Languages.Single(x => x.CultureName == "de");
            Check(vm.Rows[0].StateText == "Fertig", "existing row status changes language");
            Check(vm.Rows[0].SignatureText == strings["Signature_NoEmbeddedSignature"], "existing signature changes language");
            vm.Options.SelectedLanguage = vm.Options.Languages.Single(x => x.CultureName == "zh-Hans");
            Check(vm.Rows[0].StateText == "已完成", "Chinese satellite resource is loaded");
            vm.ChecksumOptions.Single(x => x.Algorithm == ChecksumAlgorithm.SHA512).IsSelected = true;
            vm.ReanalyseCommand.Execute(null);
            PumpUntil(() => !vm.IsBusy && vm.Rows.All(r => r.SHA512 == HashValue(ChecksumAlgorithm.SHA512)));
            Check(service.LastOptions!.Algorithms.Contains(ChecksumAlgorithm.SHA512), "reanalysis uses new options");
            vm.ClearCommand.Execute(null);
            Check(vm.Rows.Count == 0 && vm.RowsView.Count == 0 && vm.SearchText.Length == 0,
                "clear removes results and resets filters");

            var blocked = new BlockingAnalysis();
            using var busyVm = new MainViewModel(blocked, new EmptyPicker(), strings, CreateOptions(strings));
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
            using var cancelVm = new MainViewModel(cancelled, new EmptyPicker(), strings, CreateOptions(strings));
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

    private static void CheckCheckboxPersistence(LocalizationService strings)
    {
        var store = new MemoryPreferences();
        store.Save(new UserPreferences
        {
            Analysis = new AnalysisPreferences
            {
                MD5 = true, SHA1 = true, SHA256 = false, SHA384 = true, SHA512 = false,
                InspectCertificates = false, OnlineRevocation = true
            }
        });
        var saves = store.SaveCount;
        using (var vm = new MainViewModel(new ImmediateAnalysis(), new EmptyPicker(), strings, new OptionsViewModel(strings, store)))
        {
            Check(store.SaveCount == saves, "restoring checkbox state performs no writes");
            Check(vm.ChecksumOptions.Where(o => o.IsSelected).Select(o => o.Algorithm)
                .SequenceEqual(new[] { ChecksumAlgorithm.MD5, ChecksumAlgorithm.SHA1, ChecksumAlgorithm.SHA384 }),
                "main view restores each checksum independently, including disabled SHA-256");
            Check(!vm.InspectCertificates && vm.OnlineRevocation && !vm.CanChooseOnlineRevocation,
                "disabled online-revocation checkbox retains its saved selection");
            Check(vm.ShowMD5Column && vm.ShowSHA1Column && !vm.ShowSHA256Column && vm.ShowSHA384Column && !vm.ShowSHA512Column,
                "initial column visibility follows restored checksum choices");
            foreach (var option in vm.ChecksumOptions) option.IsSelected = false;
            vm.OnlineRevocation = false;
            Check(store.Saved.Analysis == new AnalysisPreferences { SHA256 = false, InspectCertificates = false },
                "unchecking options persists immediately before closing the window");
        }
        var analysis = new ImmediateAnalysis();
        using var reopened = new MainViewModel(analysis, new EmptyPicker(), strings, new OptionsViewModel(strings, store));
        Check(reopened.ChecksumOptions.All(o => !o.IsSelected) && !reopened.InspectCertificates && !reopened.OnlineRevocation,
            "restart preserves all seven checkboxes off");
        reopened.DropFilesCommand.Execute(new[] { Path.Combine(Path.GetTempPath(), "restored-options.bin") });
        PumpUntil(() => !reopened.IsBusy && reopened.Rows.Count == 1);
        Check(analysis.LastOptions is { Algorithms.Count: 0, InspectSignature: false, RevocationMode: RevocationMode.OfflineCache },
            "analysis uses restored checkbox choices");
        foreach (var option in reopened.ChecksumOptions) option.IsSelected = true;
        reopened.InspectCertificates = true;
        reopened.OnlineRevocation = true;
        Check(store.Saved.Analysis is { MD5: true, SHA1: true, SHA256: true, SHA384: true, SHA512: true,
            InspectCertificates: true, OnlineRevocation: true } && reopened.CanChooseOnlineRevocation,
            "each checksum and both signature checkboxes can be enabled and saved");
        reopened.ClearCommand.Execute(null);
        Check(store.Saved.Analysis == reopened.Options.AnalysisPreferences && reopened.ChecksumOptions.All(o => o.IsSelected),
            "clear keeps saved analysis choices");
    }

    private static void CheckFileIntake(LocalizationService strings)
    {
        var picker = new TestPicker();
        using var vm = new MainViewModel(new ImmediateAnalysis(), picker, strings, CreateOptions(strings));
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
            Check(vm.RowsView.Count == 0 && vm.Rows.Single(r => r.FullPath == fourth).SHA256 == HashValue(ChecksumAlgorithm.SHA256),
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

    private static void CheckMultipleSignatures(LocalizationService strings)
    {
        var analysis = new MultipleSignatureAnalysis();
        using var vm = new MainViewModel(analysis, new EmptyPicker(), strings, CreateOptions(strings));
        var window = new MainWindow(vm);
        try
        {
            window.Measure(new Size(1440, 900));
            window.Arrange(new Rect(0, 0, 1440, 900));
            DrainDispatcher();
            var grid = (DataGrid)window.FindName("ResultsGrid");
            var column = (DataGridTemplateColumn)grid.Columns.Single(c => ColumnLayoutBehavior.GetId(c) == "SignatureChoice");
            vm.DropFilesCommand.Execute(new[] { Path.Combine(Path.GetTempPath(), "single.exe") });
            PumpUntil(() => !vm.IsBusy && vm.Rows.Count == 1);
            Check(!vm.ShowSignatureSelectorColumn && column.Visibility == Visibility.Collapsed,
                "selector column stays hidden for single signatures");
            vm.DropFilesCommand.Execute(new[] { Path.Combine(Path.GetTempPath(), "multi.exe") });
            PumpUntil(() => !vm.IsBusy && vm.Rows.Count == 2);
            Check(vm.ShowSignatureSelectorColumn && column.Visibility == Visibility.Visible,
                "one multiply signed file reveals the bound selector column");
            var row = vm.Rows.Single(r => r.HasMultipleSignatures);
            var calls = analysis.Calls;
            // Instantiate the real cell template even when this column is scrolled out.
            var combo = (ComboBox)column.CellTemplate.LoadContent();
            combo.DataContext = row;
            DrainDispatcher();
            Check(combo.Items.Count == 2 && combo.SelectedItem == row.Signatures[0] && combo.IsEnabled,
                "real dropdown binds the signature list and primary selection");
            vm.SignatureFilterOption = vm.SignatureFilters.Single(f => f.Value == SignatureFilter.Valid);
            combo.SelectedItem = row.Signatures[1];
            DrainDispatcher();
            Check(row.SelectedSignature == row.Signatures[1] && row.Publisher == "Second publisher"
                && row.SignatureState == SignatureStatus.Untrusted && analysis.Calls == calls,
                "dropdown changes selected metadata without reanalysis");
            Check(vm.RowsView.Count == 1 && vm.ShowSignatureSelectorColumn,
                "selection refreshes the signature filter; a hidden multi-signature row keeps the column available");
            vm.ResetFiltersCommand.Execute(null);
            vm.SearchText = "Second publisher";
            Check(vm.RowsView.Count == 1, "search sees the selected certificate");
            combo.SelectedItem = row.Signatures[0];
            DrainDispatcher();
            Check(vm.RowsView.Count == 0, "search refreshes after dropdown selection");
            vm.ResetFiltersCommand.Execute(null);
            combo.DataContext = vm.Rows.Single(r => !r.HasMultipleSignatures);
            DrainDispatcher();
            Check(!combo.IsEnabled && combo.Items.Count == 1, "single-signature rows have a disabled single choice");
            foreach (var enabled in new[] { false, true })
            {
                vm.InspectCertificates = enabled;
                DrainDispatcher();
                Check(column.Visibility == (enabled ? Visibility.Visible : Visibility.Collapsed),
                    "signature selector also follows the inspection checkbox");
            }
            analysis.Multiple = false;
            vm.ReanalyseCommand.Execute(null);
            PumpUntil(() => !vm.IsBusy);
            Check(!vm.ShowSignatureSelectorColumn && column.Visibility == Visibility.Collapsed,
                "reanalysis drops obsolete multiple-signature state");
            vm.ClearCommand.Execute(null);
            Check(!vm.ShowSignatureSelectorColumn && vm.Rows.Count == 0, "clear hides the selector again");
        }
        finally { window.Close(); }
    }

    private static void CheckWindowPersistence(LocalizationService strings)
    {
        foreach (var maximize in new[] { false, true })
        {
            var store = new MemoryPreferences();
            var options = new OptionsViewModel(strings, store);
            using var vm = new MainViewModel(new ImmediateAnalysis(), new EmptyPicker(), strings, options);
            var window = new MainWindow(vm) { ShowActivated = false, ShowInTaskbar = false };
            var closed = false;
            var area = SystemParameters.WorkArea;
            Check(Math.Abs(window.Left + window.Width / 2 - (area.Left + area.Width / 2)) < 1
                && Math.Abs(window.Top + window.Height / 2 - (area.Top + area.Height / 2)) < 1,
                "normal window starts centered on the primary work area");
            try
            {
                window.Show();
                DrainDispatcher();
                var width = Math.Min(1234, area.Width);
                var height = Math.Min(789, area.Height);
                window.Width = width;
                window.Height = height;
                DrainDispatcher();
                if (maximize) window.WindowState = WindowState.Maximized;
                DrainDispatcher();
                window.WindowState = WindowState.Minimized;
                DrainDispatcher();
                window.Close();
                closed = true;
                Check(store.Saved.Window is { } saved && saved.Maximized == maximize
                    && Math.Abs(saved.Width - width) < 2 && Math.Abs(saved.Height - height) < 2,
                    "closing minimized preserves normal bounds and last non-minimized state / " + maximize);
                using var nextVm = new MainViewModel(new ImmediateAnalysis(), new EmptyPicker(), strings,
                    new OptionsViewModel(strings, store));
                var next = new MainWindow(nextVm);
                try
                {
                    Check(next.WindowState == (maximize ? WindowState.Maximized : WindowState.Normal)
                        && Math.Abs(next.Width - width) < 2 && Math.Abs(next.Height - height) < 2,
                        "next window restores saved dimensions and maximized state / " + maximize);
                }
                finally { next.Close(); }
            }
            finally { if (!closed) window.Close(); }
        }
    }

    private static void CheckHorizontalScrolling(LocalizationService strings)
    {
        using var vm = new MainViewModel(new ImmediateAnalysis(), new EmptyPicker(), strings, CreateOptions(strings));
        foreach (var option in vm.ChecksumOptions) option.IsSelected = true;
        var window = new MainWindow(vm) { ShowActivated = false, ShowInTaskbar = false };
        var trace = PresentationTraceSources.DataBindingSource;
        var previousLevel = trace.Switch.Level;
        using var messages = new StringWriter();
        using var listener = new TextWriterTraceListener(messages);
        trace.Switch.Level = SourceLevels.Error;
        trace.Listeners.Add(listener);
        try
        {
            // Showing the window exercises the production Loaded handler and the
            // actual Fluent template, including its collapsed select-all button.
            window.Show();
            DrainDispatcher();
            var grid = (DataGrid)window.FindName("ResultsGrid");
            var viewer = (ScrollViewer)grid.Template.FindName("DG_ScrollViewer", grid);
            var button = VisualChildren(grid).OfType<Button>().Single(b =>
                b.Command == DataGrid.SelectAllCommand && ReferenceEquals(b.TemplatedParent, viewer));
            var binding = BindingOperations.GetBinding(button, FrameworkElement.WidthProperty)!;
            Check(binding.Converter is not null && ReferenceEquals(binding.Source, grid)
                && binding.Mode == BindingMode.OneWay && binding.Path.Path == nameof(DataGrid.CellsPanelHorizontalOffset),
                "native select-all width uses a one-way nonnegative offset binding");
            Check(button.Visibility == Visibility.Collapsed, "hidden row headers still have a safely bound corner button");
            var expression = BindingOperations.GetBindingExpression(button, FrameworkElement.WidthProperty);
            DataGridScrollFix.Apply(grid);
            Check(ReferenceEquals(expression, BindingOperations.GetBindingExpression(button, FrameworkElement.WidthProperty)),
                "repeated initialization leaves the existing corrected binding intact");

            // Feed reported values through a real Width binding: a converter
            // unit call alone would not catch WPF rejecting its result.
            var source = new OffsetProbe();
            var probe = new Button();
            probe.SetBinding(FrameworkElement.WidthProperty, new Binding(nameof(OffsetProbe.Value))
            { Source = source, Mode = BindingMode.OneWay, Converter = binding.Converter });
            foreach (var value in new[] { -0.324324324324607, -0.007112375532415172, -4.547473508864641E-13,
                -100d, 0d, 42.75, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            {
                source.Value = value;
                DrainDispatcher();
                var expected = double.IsFinite(value) && value > 0 ? value : 0;
                Check(probe.Width == expected
                    && !BindingOperations.GetBindingExpression(probe, FrameworkElement.WidthProperty)!.HasError,
                    "Width binding accepts normalized offset / " + value);
            }

            vm.DropFilesCommand.Execute(new[] { Path.Combine(Path.GetTempPath(), "scroll-test-a.bin"),
                Path.Combine(Path.GetTempPath(), "scroll-test-b.bin") });
            PumpUntil(() => !vm.IsBusy && vm.Rows.Count == 2);
            var template = grid.Template;
            Check(viewer.ScrollableWidth > 0, "scroll fixture has horizontal overflow");
            foreach (var offset in new[] { 0d, 0.1, 0.324324324324607, 100.75, 543.125, 10000d, 17.3, 0d })
            {
                viewer.ScrollToHorizontalOffset(offset);
                grid.UpdateLayout();
                DrainDispatcher();
                Check(double.IsFinite(button.Width) && button.Width >= 0
                    && !BindingOperations.GetBindingExpression(button, FrameworkElement.WidthProperty)!.HasError,
                    "native corner width stays valid after horizontal scrolling / " + offset);
            }
            Check(ReferenceEquals(template, grid.Template) && grid.FrozenColumnCount == 2
                && grid.EnableRowVirtualization && grid.EnableColumnVirtualization
                && grid.Columns.All(column => column.Width.IsAuto),
                "scroll fix preserves native template, frozen columns, virtualization and automatic widths");
            trace.Flush();
            Check(!messages.ToString().Contains(nameof(DataGrid.CellsPanelHorizontalOffset), StringComparison.Ordinal),
                "horizontal scrolling produces no CellsPanelHorizontalOffset binding errors");
        }
        finally
        {
            trace.Listeners.Remove(listener);
            trace.Switch.Level = previousLevel;
            window.Close();
        }
    }

    private static IEnumerable<DependencyObject> VisualChildren(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            yield return child;
            foreach (var descendant in VisualChildren(child)) yield return descendant;
        }
    }

    private sealed class OffsetProbe : DependencyObject
    {
        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
            nameof(Value), typeof(double), typeof(OffsetProbe), new PropertyMetadata(0d));
        public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    }

    private static void CheckPresentation(MainWindow window, MainViewModel vm, ImmediateAnalysis analysis)
    {
        var grid = (DataGrid)window.FindName("ResultsGrid");
        var columns = grid.Columns.OfType<DataGridTextColumn>()
            .ToDictionary(column => ((Binding)column.Binding).Path.Path);
        var results = vm.Rows.Select(row => row.Result).ToArray();
        var analysisCalls = analysis.Calls;
        string[] certificateColumns = ["SigningTimeText", "SignatureText", "Publisher", "Subject", "Issuer", "SerialNumber",
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
        void CheckOrder() => Check(grid.Columns.OrderBy(column => column.DisplayIndex).Select(ColumnLayoutBehavior.GetId)
            .SequenceEqual(vm.Options.Columns.Select(column => column.Id)), "grid follows the complete options order");
        DrainDispatcher();
        CheckOrder();
        var md5 = vm.Options.Columns.Single(column => column.Id == "MD5");
        vm.Options.SelectedColumn = md5;
        vm.Options.MoveFirstCommand.Execute(null);
        CheckOrder();
        Check(columns["MD5"].DisplayIndex == 0 && columns["MD5"].Visibility == Visibility.Collapsed,
            "hidden columns can move across the frozen boundary");
        vm.ChecksumOptions.Single(option => option.Algorithm == ChecksumAlgorithm.MD5).IsSelected = true;
        DrainDispatcher();
        Check(columns["MD5"].DisplayIndex == 0 && columns["MD5"].Visibility == Visibility.Visible,
            "revealed columns retain the chosen position");
        vm.Options.SetColumnOrder(vm.Options.Columns.Reverse().Select(column => column.Id));
        CheckOrder();
        vm.Options.ResetOrderCommand.Execute(null);
        CheckOrder();
        vm.ChecksumOptions.Single(option => option.Algorithm == ChecksumAlgorithm.MD5).IsSelected = false;
        var optionsWindow = new OptionsWindow(vm.Options);
        var originalLanguage = vm.Options.SelectedLanguage;
        try
        {
            DrainDispatcher();
            var languageBinding = BindingOperations.GetBinding(optionsWindow, FrameworkElement.LanguageProperty);
            Check(languageBinding?.ConverterCulture is not null, "string Language binding has an explicit conversion culture");
            foreach (var language in vm.Options.Languages)
            {
                vm.Options.SelectedLanguage = language;
                DrainDispatcher();
                Check(string.Equals(optionsWindow.Language.IetfLanguageTag, language.CultureName, StringComparison.OrdinalIgnoreCase),
                    $"options Language converts without using the target Language / {language.CultureName}");
                Check(optionsWindow.Title == LocalizationService.Instance["OptionsTitle"],
                    $"conversion culture does not force English UI text / {language.CultureName}");
            }
            vm.Options.SelectedLanguage = originalLanguage;
            DrainDispatcher();
            var selector = (ComboBox)optionsWindow.FindName("HashFormatSelector");
            selector.SelectedIndex = vm.Options.HashFormats.ToList().FindIndex(option => option.Value == HashFormat.Base64);
            DrainDispatcher();
            var expected = Convert.ToBase64String(Convert.FromHexString(HashValue(ChecksumAlgorithm.SHA256)));
            Check(vm.Rows.All(row => row.SHA256 == expected && row.Details.Contains(expected)),
                "format dropdown updates existing grid values and details");
            Check(analysis.Calls == analysisCalls, "switching format performs no new analysis");
            vm.SearchText = expected;
            Check(vm.RowsView.Count == 2, "formatted digests are searchable");
            selector.SelectedIndex = vm.Options.HashFormats.ToList().FindIndex(option => option.Value == HashFormat.HexUpper);
            DrainDispatcher();
            Check(vm.RowsView.Count == 0, "active filters refresh after a representation change");
            vm.ResetFiltersCommand.Execute(null);
            Check(vm.Rows.Select(row => row.Result).Zip(results).All(pair => ReferenceEquals(pair.First, pair.Second)),
                "presentation changes preserve the original analysis results");
        }
        finally { vm.Options.SelectedLanguage = originalLanguage; optionsWindow.Close(); }
        grid.UnselectAll();
        vm.Options.FitColumnsCommand.Execute(null);
        DrainDispatcher();
        LayoutGrid();
        Check(grid.AlternationCount == 0 && ((SolidColorBrush)grid.Background).Color == Colors.White,
            "table has a uniform light background without alternating rows");
        Check(grid.Columns.All(column => column.Width.IsAuto && column.MinWidth > 0 && column.MaxWidth < 1000),
            "all columns use bounded automatic sizing");
        Check(grid.Columns.Where(column => column.Visibility == Visibility.Visible)
            .All(column => column.ActualWidth >= column.MinWidth && column.ActualWidth <= column.MaxWidth),
            "visible columns respect minimum and maximum widths");
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
    private static OptionsViewModel CreateOptions(LocalizationService strings) => new(strings, new MemoryPreferences());
    private sealed class MemoryPreferences : IPreferencesStore
    {
        public UserPreferences Saved { get; private set; } = new();
        public int SaveCount { get; private set; }
        public UserPreferences Load() => Saved;
        public void Save(UserPreferences preferences) { Saved = preferences; SaveCount++; }
    }
    private sealed class TestPicker : IFilePickerService
    {
        public string[] Files { get; set; } = [];
        public string[] PickFiles(string title, string allFilesLabel) => Files;
    }
    private sealed class ImmediateAnalysis : IFileAnalysisService
    {
        public AnalysisOptions? LastOptions { get; private set; }
        public int Calls { get; private set; }
        public Task<FileAnalysisResult> AnalyzeAsync(string path, AnalysisOptions options,
            IProgress<AnalysisProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            LastOptions = options;
            Calls++;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Result(path, options));
        }
    }
    private sealed class MultipleSignatureAnalysis : IFileAnalysisService
    {
        public int Calls;
        public bool Multiple = true;
        public Task<FileAnalysisResult> AnalyzeAsync(string path, AnalysisOptions options,
            IProgress<AnalysisProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            Calls++;
            var time = new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero);
            var certificate = new CertificateInfo("First publisher", "Subject", "Issuer", "01", "00FF", "00FF",
                time.AddYears(-1), time.AddYears(1), "sha256RSA");
            var entries = new List<EmbeddedSignature>
            {
                new(0, SignatureStatus.Valid, certificate, 0, null, "SHA-256", new(time, TimestampStatus.Verified))
            };
            if (Multiple && Path.GetFileName(path) == "multi.exe")
                entries.Add(new(1, SignatureStatus.Untrusted, certificate with { Publisher = "Second publisher" },
                    unchecked((int)0x800B0109), null, "SHA-1", new(time.AddDays(1), TimestampStatus.Unverified)));
            return Task.FromResult(Result(path, options) with
            {
                Signature = new(SignatureStatus.Valid, options.RevocationMode, certificate, 0)
                { Signatures = entries.AsReadOnly() }
            });
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
    private static string HashValue(ChecksumAlgorithm algorithm) => Convert.ToHexString(algorithm switch
    {
        ChecksumAlgorithm.MD5 => System.Security.Cryptography.MD5.HashData("abc"u8),
        ChecksumAlgorithm.SHA1 => System.Security.Cryptography.SHA1.HashData("abc"u8),
        ChecksumAlgorithm.SHA256 => System.Security.Cryptography.SHA256.HashData("abc"u8),
        ChecksumAlgorithm.SHA384 => System.Security.Cryptography.SHA384.HashData("abc"u8),
        ChecksumAlgorithm.SHA512 => System.Security.Cryptography.SHA512.HashData("abc"u8),
        _ => throw new ArgumentOutOfRangeException(nameof(algorithm))
    });
    private static FileAnalysisResult Result(string path, AnalysisOptions options) => new(
        path, 3, DateTimeOffset.UtcNow,
        options.Algorithms.ToDictionary(a => a, HashValue),
        new SignatureResult(SignatureStatus.NoEmbeddedSignature, options.RevocationMode));
}
