using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;
using FileScope.App.Behaviors;
using FileScope.App.Controls;
using FileScope.App.Presentation;
using System.Windows.Threading;
using FileScope.App;
using FileScope.App.Localization;
using FileScope.App.Services;
using FileScope.App.ViewModels;
using FileScope.Core.Abstractions;
using FileScope.Core.Models;
using FileScope.Core.Files;
using FileScope.Core.Export;

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
            // pumped. A plain host uses the same theme/resources without startup
            // composition. check_structure.py guards the theme against App.xaml.
#pragma warning disable WPF0001 // Match the ThemeMode already used in App.xaml; the SDK still marks its C# API experimental.
            app = new Application
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown,
                ThemeMode = ThemeMode.Light
            };
#pragma warning restore WPF0001
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("/FileScope.App;component/Styles/Controls.xaml", UriKind.Relative)
            });
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("/FileScope.App;component/Styles/Themes/LightThemePalette.xaml", UriKind.Relative)
            });
            app.Resources.Add("BoolToVisibility", new BooleanToVisibilityConverter());
            // Commands are invoked before PumpUntil enters its dispatcher frame.
            // Their await continuations must already capture the UI context here.
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            var strings = LocalizationService.Instance;
            CheckThemeService(app);
            CheckThemeSwitching(app, strings);
            CheckAboutDialog(app, strings);
            CheckShortcutsDialog(app, strings);
            CheckFileContextMenu(app, strings);
            CheckFileMoves(app, strings);
            CheckPathPresentation(strings);
            CheckCheckboxPersistence(strings);
            CheckMetadataOptions(strings);
            CheckFileIntake(strings);
            CheckFolderAndExport(strings);
            CheckDroppedFolders(strings);
            CheckFileOperationCancellation(strings);
            CheckMultipleSignatures(strings);
            CheckDiagnostics(strings);
            CheckRunSummaries(strings);
            CheckBufferedProgress(strings);
            CheckExceptValidFilter(strings);
            CheckWindowPersistence(strings);
            CheckOptionsWindowPersistence(strings);
            CheckHorizontalScrolling(strings);
            CheckTemplateCellBindings(strings);
            CheckScrollBarSizing();
            var service = new ImmediateAnalysis();
            using var vm = CreateMainViewModel(service, new EmptyPicker(), strings, CreateOptions(strings));
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
            Check(vm.Rows[0].SignatureText == "3 | " + strings["Signature_NoEmbeddedSignature"], "existing signature changes language and retains its code");
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
            using var busyVm = CreateMainViewModel(blocked, new EmptyPicker(), strings, CreateOptions(strings));
            busyVm.DropFilesCommand.Execute(new[] { first });
            PumpUntil(() => blocked.Started.Task.IsCompleted);
            Check(busyVm.IsBusy && !busyVm.DropFilesCommand.CanExecute(new[] { second }),
                "concurrent drop is disabled during a batch");
            Check(!busyVm.ClearCommand.CanExecute(null), "clear is disabled during analysis");
            busyVm.ClearCommand.Execute(null);
            Check(busyVm.IsBusy && busyVm.Rows.Count == 1 && !blocked.Token.IsCancellationRequested,
                "disabled clear neither removes rows nor cancels analysis");
            busyVm.CancelCommand.Execute(null);
            Check(blocked.Token.IsCancellationRequested, "cancel requests cancellation");
            Check(busyVm.IsBusy && !busyVm.ClearCommand.CanExecute(null),
                "clear stays disabled while cancellation waits for a non-interruptible strategy");
            blocked.Release.TrySetResult();
            PumpUntil(() => !busyVm.IsBusy);
            Check(busyVm.Rows.Count == 1 && busyVm.Rows[0].State == RowStatus.Cancelled
                && busyVm.ClearCommand.CanExecute(null), "cancelled analysis keeps rows and enables clear");
            busyVm.ClearCommand.Execute(null);
            Check(busyVm.Rows.Count == 0 && busyVm.ShowEmptyState, "clear empties the grid after cancellation finishes");
            blocked.Progress?.Report(new AnalysisProgress(AnalysisStage.Reading, 1, 1));
            DrainDispatcher();
            Check(busyVm.Rows.Count == 0 && busyVm.Progress == 0 && busyVm.CurrentFile.Length == 0,
                "late progress cannot restore cleared state");

            var cancelled = new BlockingAnalysis();
            using var cancelVm = CreateMainViewModel(cancelled, new EmptyPicker(), strings, CreateOptions(strings));
            cancelVm.DropFilesCommand.Execute(new[] { first, second });
            PumpUntil(() => cancelled.Started.Task.IsCompleted);
            cancelVm.CancelCommand.Execute(null);
            cancelled.Release.TrySetResult();
            PumpUntil(() => !cancelVm.IsBusy);
            Check(cancelVm.Rows.Count == 2, "cancel keeps rows");
            Check(cancelVm.Rows.Any(r => r.State == RowStatus.Cancelled), "unprocessed rows are marked cancelled");
            Check(cancelVm.ReanalyseCommand.CanExecute(null), "cancelled batch can be retried");
            Console.WriteLine();
            ConsoleColor backgroundBackup = Console.BackgroundColor;
            ConsoleColor foregroundBackup = Console.ForegroundColor;
            Console.BackgroundColor = ConsoleColor.White;
            Console.ForegroundColor = ConsoleColor.DarkGreen;
            Console.WriteLine($"PASS: {_passed} UI / MVVM assertions.");
            Console.BackgroundColor = backgroundBackup;
            Console.ForegroundColor = foregroundBackup;
            Console.WriteLine("=======================");
            Console.WriteLine();
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

    private static void CheckShortcutsDialog(Application app, LocalizationService strings)
    {
        strings.SetCulture("en");
        var themes = new ThemeService(app);
        var store = new MemoryPreferences();
        using var vm = CreateMainViewModel(new ImmediateAnalysis(), new EmptyPicker(), strings,
            new OptionsViewModel(strings, store, themes));
        var main = new MainWindow(vm) { ShowActivated = false, ShowInTaskbar = false };
        var shortcuts = new ShortcutsWindow { ShowActivated = false };
        var errors = new StringWriter();
        using var listener = new TextWriterTraceListener(errors);
        var bindingSource = PresentationTraceSources.DataBindingSource;
        var previousLevel = bindingSource.Switch.Level;
        bindingSource.Switch.Level = SourceLevels.Error;
        bindingSource.Listeners.Add(listener);
        try
        {
            main.Show();
            shortcuts.Owner = main;
            DrainDispatcher();
            var button = (Button)main.FindName("ShortcutsButton");
            var keys = main.InputBindings.OfType<KeyBinding>().ToArray();
            var help = keys.Single(binding => binding.Key == Key.F1);
            Check(help.Modifiers == ModifierKeys.None && ReferenceEquals(help.Command, ApplicationCommands.Help)
                && ReferenceEquals(button.Command, help.Command) && button.IsEnabled,
                "the shortcuts button and F1 use the same enabled help command");
            Check(keys.Any(binding => binding.Key == Key.O && binding.Modifiers == ModifierKeys.Control && ReferenceEquals(binding.Command, vm.BrowseCommand))
                && keys.Any(binding => binding.Key == Key.L && binding.Modifiers == ModifierKeys.Control && ReferenceEquals(binding.Command, vm.ClearCommand))
                && keys.Any(binding => binding.Key == Key.Escape && ReferenceEquals(binding.Command, vm.CancelCommand)),
                "the cheat sheet's file, clear and cancel actions match the actual application key bindings");

            var opened = false;
            void CloseNextModal()
            {
                app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
                {
                    var dialog = app.Windows.OfType<ShortcutsWindow>().SingleOrDefault(window => window.IsVisible);
                    if (dialog == null) return;
                    opened = ReferenceEquals(dialog.Owner, main) && ComponentDispatcher.IsThreadModal && !dialog.ShowInTaskbar;
                    ((Button)dialog.FindName("CloseShortcutsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }));
            }
            CloseNextModal();
            var peer = new ButtonAutomationPeer(button);
            ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
            PumpUntil(() => opened && !app.Windows.OfType<ShortcutsWindow>().Any(window => window.IsVisible));
            Check(opened, "invoking the real shortcuts button opens an owned modal dialog and Close returns");
            opened = false;
            CloseNextModal();
            ((RoutedCommand)help.Command).Execute(null, main);
            Check(opened && !app.Windows.OfType<ShortcutsWindow>().Any(window => window.IsVisible),
                "the F1-bound routed command opens and closes the same owned help dialog");

            var saves = store.SaveCount;
            shortcuts.Show();
            DrainDispatcher();
            var close = (Button)shortcuts.FindName("CloseShortcutsButton");
            Check(close.IsCancel && close.IsDefault && !shortcuts.ShowInTaskbar && shortcuts.ResizeMode == ResizeMode.CanResizeWithGrip,
                "shortcuts support Escape, Enter, resizing and no separate taskbar entry");
            var entries = new[] { "Open", "Clear", "Cancel", "Help", "TableEnds", "Page", "RowEnds", "Rows", "SelectAll", "Extend", "Copy", "ContextMenu" };
            var groups = new[] { "Actions", "Navigation", "Selection" };
            foreach (var language in strings.Languages)
            {
                strings.SetCulture(language.CultureName);
                DrainDispatcher();
                Check(shortcuts.Title == strings["ShortcutsTitle"]
                    && string.Equals(shortcuts.Language.IetfLanguageTag, strings.Culture.IetfLanguageTag, StringComparison.OrdinalIgnoreCase)
                    && ((TextBlock)shortcuts.FindName("ShortcutsHintText")).Text == strings["ShortcutsHint"]
                    && System.Windows.Automation.AutomationProperties.GetName(button) == strings["ShortcutsTitle"],
                    "the existing shortcut window and accessible toolbar label follow the language / " + language.CultureName);
                Check(entries.All(id => ((TextBlock)shortcuts.FindName("Shortcut" + id + "Keys")).Text == strings["Shortcut" + id + "Keys"]
                        && ((TextBlock)shortcuts.FindName("Shortcut" + id + "Action")).Text == strings["Shortcut" + id + "Action"])
                    && groups.All(group =>
                        ((TextBlock)shortcuts.FindName("ShortcutsGroup" + group)).Text == strings["ShortcutsGroup" + group])
                    && close.Content.ToString() == strings["Close"],
                    "all twelve shortcut pairs, section headings and Close are translated in place / " + language.CultureName);
            }
            foreach (var theme in new[] { AppTheme.Dark, AppTheme.Light })
            {
                themes.Apply(theme);
                DrainDispatcher();
                var expected = theme == AppTheme.Dark ? Color.FromRgb(0xB1, 0xB1, 0xBA) : Color.FromRgb(0x59, 0x65, 0x79);
                Check(((SolidColorBrush)((TextBlock)shortcuts.FindName("ShortcutsHintText")).Foreground).Color == expected,
                    "the existing shortcut window follows the current palette / " + theme);
            }
            shortcuts.Width = shortcuts.MinWidth;
            shortcuts.Height = shortcuts.MinHeight;
            DrainDispatcher();
            shortcuts.UpdateLayout();
            var scroll = (ScrollViewer)shortcuts.FindName("ShortcutsScrollViewer");
            Check(scroll.ScrollableHeight > 0 && scroll.HorizontalScrollBarVisibility == ScrollBarVisibility.Disabled && close.IsVisible,
                "a small shortcut window scrolls its list while keeping Close outside the scrolling area");
            Check(store.SaveCount == saves, "viewing and translating the cheat sheet does not write preferences");
            Check(errors.ToString().Length == 0, "shortcuts, themes and translations produce no WPF binding errors: " + errors);
            close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(!shortcuts.IsVisible, "the shortcut Close button also closes a modeless test window");
        }
        finally
        {
            shortcuts.Close();
            main.Close();
            themes.Apply(AppTheme.Light);
            strings.SetCulture("en");
            bindingSource.Listeners.Remove(listener);
            bindingSource.Switch.Level = previousLevel;
        }
    }

    private static void CheckPathPresentation(LocalizationService strings)
    {
        strings.SetCulture("en");
        var store = new MemoryPreferences();
        var options = new OptionsViewModel(strings, store, new RecordingThemeService());
        using var vm = CreateMainViewModel(new ImmediateAnalysis(), new EmptyPicker(), strings, options);
        var window = new MainWindow(vm) { ShowActivated = false, ShowInTaskbar = false };
        var settings = new OptionsWindow(options) { ShowActivated = false, ShowInTaskbar = false };
        try
        {
            window.Show();
            // WPF requires the owner to have been shown before assigning Owner.
            settings.Owner = window;
            settings.Show();
            var root = Path.Combine(Path.GetTempPath(), "FileScope-path-ui");
            var firstPath = Path.Combine(root, new string('a', 100), new string('b', 100), "first.exe");
            vm.DropFilesCommand.Execute(new[] { firstPath, Path.Combine(root, "second.exe") });
            PumpUntil(() => !vm.IsBusy && vm.Rows.Count == 2);
            var grid = (DataGrid)window.FindName("ResultsGrid");
            var pathColumn = grid.Columns.Single(column => ColumnLayoutBehavior.GetId(column) == "Path");
            var nameColumn = grid.Columns.Single(column => ColumnLayoutBehavior.GetId(column) == "Name");
            var checkBox = (CheckBox)settings.FindName("PathAutoWidthCheckBox");
            var header = (TextBlock)window.FindName("DetailsHeaderPath");
            var label = (TextBlock)window.FindName("DetailsHeaderLabel");
            var details = (TextBox)window.FindName("SelectedFileDetails");
            DrainDispatcher();
            Check(pathColumn == grid.Columns[^1] && pathColumn.DisplayIndex == grid.Columns.Count - 1
                && checkBox.IsChecked == true && pathColumn.Width.IsAuto && double.IsPositiveInfinity(pathColumn.MaxWidth),
                "path is the final default column with automatic content width enabled");
            grid.SelectedItem = vm.Rows[0];
            DrainDispatcher();
            Check(header.Text == firstPath && label.Text.EndsWith(':') && header.Margin.Left >= 16 && details.Text.Contains(firstPath),
                "one selected file displays its full path after the spaced details label and colon");
            grid.SelectedItems.Add(vm.Rows[1]);
            DrainDispatcher();
            Check(header.Text.Length == 0 && details.Text.Length == 0 && vm.SingleSelectedRow is null,
                "multiple selected files do not present one file as the complete selection");
            grid.UnselectAll();
            DrainDispatcher();
            Check(header.Text.Length == 0 && !vm.ShowFilePropertiesCommand.CanExecute(null) && !vm.ShowInExplorerCommand.CanExecute(null),
                "empty selection clears the details header and disables single-file shell actions");
            grid.SelectedItem = vm.Rows[0];
            var relocated = Path.Combine(root, "unsigned", "first.exe");
            vm.Rows[0].Relocate(relocated);
            DrainDispatcher();
            Check(header.Text == relocated && details.Text.Contains(relocated), "relocation immediately updates the selected file's header and details");
            nameColumn.Width = new DataGridLength(300);
            checkBox.IsChecked = false;
            DrainDispatcher();
            Check(!options.PathAutoWidth && !store.Saved.PathAutoWidth && pathColumn.Width.IsAbsolute
                && pathColumn.Width.Value == 320 && pathColumn.MaxWidth == 600 && nameColumn.Width.Value == 300,
                "the real checkbox saves bounded path sizing without resetting another column's manual width");
            pathColumn.Width = new DataGridLength(400);
            foreach (var culture in new[] { "de", "en", "zh-Hans" })
            {
                strings.SetCulture(culture);
                options.FitColumnsCommand.Execute(null);
                DrainDispatcher();
                Check(pathColumn.Width.IsAbsolute && pathColumn.Width.Value == 400 && checkBox.Content.ToString() == strings["PathAutoWidth"]
                    && label.Text == strings["DetailsTitle"],
                    "language and global fit preserve the opted-out path width and translate the new UI / " + culture);
            }
            options.SelectedColumn = options.Columns.Single(column => column.Id == "Path");
            options.MoveFirstCommand.Execute(null);
            DrainDispatcher();
            Check(pathColumn.DisplayIndex == 0 && pathColumn.Width.Value == 400 && pathColumn.MaxWidth == 600,
                "moving the path to the frozen front keeps its bounded manual width");
            checkBox.IsChecked = true;
            DrainDispatcher();
            Check(options.PathAutoWidth && store.Saved.PathAutoWidth && pathColumn.Width.IsAuto
                && double.IsPositiveInfinity(pathColumn.MaxWidth) && pathColumn.DisplayIndex == 0,
                "re-enabling automatic path width preserves the chosen column position");
            options.ResetOrderCommand.Execute(null);
            DrainDispatcher();
            Check(pathColumn.DisplayIndex == grid.Columns.Count - 1 && options.Columns[^1].Id == "Path",
                "resetting a saved custom order returns the path to the end");
            vm.ClearCommand.Execute(null);
            DrainDispatcher();
            Check(header.Text.Length == 0 && details.Text.Length == 0, "Clear removes the previously selected path and details");
        }
        finally { settings.Close(); window.Close(); strings.SetCulture("en"); }
    }

    private static void CheckFileMoves(Application app, LocalizationService strings)
    {
        strings.SetCulture("en");
        var uiThread = Environment.CurrentManagedThreadId;
        var analysis = new ImmediateAnalysis();
        var mover = new RecordingFileMover();
        var reports = new RecordingMoveReports();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        mover.OnMove = (paths, folder, token) =>
        {
            entered.Set();
            using var registration = token.Register(release.Set);
            release.Wait(CancellationToken.None); // Cancellation releases the fake and returns partial results.
            return paths.Select((path, index) => new FileMoveResult(path,
                Path.Combine(Path.GetDirectoryName(path)!, folder, Path.GetFileName(path)),
                index == 0 ? FileMoveOutcome.Moved : token.IsCancellationRequested ? FileMoveOutcome.Cancelled : FileMoveOutcome.TargetExists)).ToArray();
        };
        using var vm = CreateMainViewModel(analysis, new EmptyPicker(), strings, CreateOptions(strings), fileMover: mover, moveReports: reports);
        var window = new MainWindow(vm) { ShowActivated = false, ShowInTaskbar = false };
        var previousMain = app.MainWindow;
        app.MainWindow = window;
        var themes = new ThemeService(app);
        ContextMenu? menu = null;
        FileMoveReportWindow? reportWindow = null;
        var errors = new StringWriter();
        using var listener = new TextWriterTraceListener(errors);
        var bindingSource = PresentationTraceSources.DataBindingSource;
        var previousLevel = bindingSource.Switch.Level;
        bindingSource.Switch.Level = SourceLevels.Error;
        bindingSource.Listeners.Add(listener);
        try
        {
            window.Show();
            var directory = Path.Combine(Path.GetTempPath(), "FileScope-move-ui-" + Guid.NewGuid().ToString("N"));
            var paths = new[] { Path.Combine(directory, "first.exe"), Path.Combine(directory, "second.exe"), Path.Combine(directory, "third.exe") };
            vm.DropFilesCommand.Execute(paths);
            PumpUntil(() => !vm.IsBusy && vm.Rows.Count == 3);
            var first = vm.Rows[0];
            var second = vm.Rows[1];
            var third = vm.Rows[2];
            var grid = (DataGrid)window.FindName("ResultsGrid");
            grid.UnselectAll();
            grid.SelectedItems.Add(first);
            grid.SelectedItems.Add(second);
            grid.ScrollIntoView(first);
            window.UpdateLayout();
            DrainDispatcher();
            var container = (DataGridRow)grid.ItemContainerGenerator.ContainerFromItem(first);
            menu = container.ContextMenu;
            menu.PlacementTarget = container;
            menu.IsOpen = true;
            DrainDispatcher();
            var moveItem = MenuFor(menu, vm.MoveSelectedCommand);
            Check(vm.CanMoveSelected && moveItem.Visibility == Visibility.Visible && moveItem.IsEnabled
                && ReferenceEquals(moveItem.Command, vm.MoveSelectedCommand) && ReferenceEquals(menu.DataContext, vm),
                "real grid multi-selection enables the row menu's move command for identical signature results");
            Check(!vm.ShowFilePropertiesCommand.CanExecute(null) && !vm.ShowInExplorerCommand.CanExecute(null),
                "single-file shell actions are disabled for a move group with multiple selected rows");
            Check(ReferenceEquals(menu.Items[0], moveItem) && menu.Items[1] is Separator { Visibility: Visibility.Visible },
                "the move action is first and separated from shell actions");
            third.Complete(third.Result! with { Signature = new SignatureResult(SignatureStatus.Revoked, RevocationMode.OfflineCache) });
            grid.SelectedItems.Add(third);
            DrainDispatcher();
            Check(!vm.CanMoveSelected && moveItem.Visibility == Visibility.Collapsed
                && ((Separator)menu.Items[1]).Visibility == Visibility.Collapsed,
                "mixed signatures hide both the move action and its separator");
            vm.MoveSelectedCommand.Execute(null);
            Check(mover.Sources.Length == 0, "a hidden move action also refuses programmatic execution");
            third.Reset();
            DrainDispatcher();
            Check(!vm.CanMoveSelected && moveItem.Visibility == Visibility.Collapsed,
                "an unanalysed selected row also hides the move menu item");
            grid.SelectedItems.Remove(third);
            strings.SetCulture("de");
            DrainDispatcher();
            Check(vm.CanMoveSelected && moveItem.IsEnabled && moveItem.Command.CanExecute(moveItem.CommandParameter)
                && moveItem.Header.ToString() == strings.Format("MoveSelectedMenu", "ohnesig"),
                "a live move menu remains enabled after language refresh and row recycling");
            var hash = first.SHA256;
            InvokeMenuItem(moveItem);
            PumpUntil(() => entered.IsSet);
            Check(vm.IsBusy && vm.IsProgressIndeterminate && !vm.CanMoveSelected
                && !vm.ClearCommand.CanExecute(null) && vm.StatusText.StartsWith(strings["MovingFiles"], StringComparison.Ordinal),
                "moving runs as a cancellable background job and disables conflicting commands");
            menu.IsOpen = false;
            release.Set();
            PumpUntil(() => !vm.IsBusy && reports.Results != null);
            var destination = Path.Combine(directory, "ohnesig", "first.exe");
            Check(mover.Sources.SequenceEqual(paths.Take(2)) && mover.Folder == "ohnesig"
                && mover.Listed!.SetEquals(paths) && mover.ThreadId != uiThread && reports.ThreadId == uiThread,
                "move inputs are captured on the UI thread, files move on a worker, and the report returns to the UI");
            Check(first.FullPath == destination && first.Result!.FullPath == destination && first.SHA256 == hash
                && second.FullPath == paths[1] && reports.Results!.Select(result => result.Outcome).SequenceEqual([FileMoveOutcome.Moved, FileMoveOutcome.TargetExists]),
                "only successfully moved rows get new paths and all outcomes reach the report");
            var calls = analysis.Calls;
            vm.DropFilesCommand.Execute(new[] { destination });
            PumpUntil(() => !vm.IsBusy);
            Check(vm.Rows.Count == 3 && analysis.Calls == calls, "a moved destination stays protected by intake deduplication");
            vm.DropFilesCommand.Execute(new[] { paths[0] });
            PumpUntil(() => !vm.IsBusy && vm.Rows.Count == 4);
            Check(vm.Rows.Count == 4 && analysis.Calls == calls + 1, "a successfully moved source path becomes available for a new file");

            // A separate batch proves cancellation retains a completed move and the remaining rows.
            entered.Reset();
            release.Reset();
            grid.UnselectAll();
            grid.SelectedItems.Add(second);
            grid.SelectedItems.Add(vm.Rows[3]);
            vm.MoveSelectedCommand.Execute(null);
            PumpUntil(() => entered.IsSet);
            vm.CancelCommand.Execute(null);
            PumpUntil(() => !vm.IsBusy && reports.Results!.Any(result => result.Outcome == FileMoveOutcome.Cancelled));
            Check(second.FullPath == Path.Combine(directory, "ohnesig", "second.exe") && vm.Rows[3].FullPath == paths[0]
                && reports.Results!.Select(result => result.Outcome).SequenceEqual([FileMoveOutcome.Moved, FileMoveOutcome.Cancelled]),
                "cancellation keeps successful move paths and reports each unattempted file");
            var reportResults = reports.Results!;
            var modalOpened = false;
            app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
            {
                var dialog = app.Windows.OfType<FileMoveReportWindow>().SingleOrDefault(candidate => candidate.IsVisible);
                if (dialog == null) return;
                modalOpened = ReferenceEquals(dialog.Owner, window) && ComponentDispatcher.IsThreadModal && !dialog.ShowInTaskbar;
                ((Button)dialog.FindName("CloseMoveReportButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }));
            new FileMoveReportService().Show(reportResults);
            Check(modalOpened && !app.Windows.OfType<FileMoveReportWindow>().Any(candidate => candidate.IsVisible),
                "the real move report service opens an owned modal window and its Close button returns");
            reportWindow = new FileMoveReportWindow(reportResults) { Owner = window, ShowActivated = false };
            reportWindow.Show();
            var reportText = (TextBox)reportWindow.FindName("MoveReportText");
            Check(reportText.IsReadOnly && reportText.HorizontalScrollBarVisibility == ScrollBarVisibility.Auto
                && reportText.VerticalScrollBarVisibility == ScrollBarVisibility.Auto && reportWindow.ResizeMode == ResizeMode.CanResize,
                "the move report is resizable with selectable read-only text and both scrollbars");
            foreach (var culture in new[] { "de", "en", "zh-Hans" })
            {
                strings.SetCulture(culture);
                DrainDispatcher();
                Check(reportWindow.Title == strings["MoveReportTitle"] && reportText.Text == FileMoveReport.Format(reportResults, strings)
                    && ((TextBlock)reportWindow.FindName("MoveSummaryText")).Text == FileMoveReport.Summary(reportResults, strings)
                    && ((Button)reportWindow.FindName("CloseMoveReportButton")).Content.ToString() == strings["Close"],
                    "the existing move report updates title, counts, details and Close together / " + culture);
            }
            foreach (var theme in new[] { AppTheme.Dark, AppTheme.Light })
            {
                themes.Apply(theme);
                DrainDispatcher();
                Check(((SolidColorBrush)((TextBlock)reportWindow.FindName("MoveSummaryText")).Foreground).Color
                        == ((SolidColorBrush)app.FindResource("MutedBrush")).Color,
                    "the existing move report follows the current palette / " + theme);
            }
            Check(errors.ToString().Length == 0, "move menu and report have no WPF binding errors: " + errors);
        }
        finally
        {
            release.Set();
            if (menu != null) menu.IsOpen = false;
            reportWindow?.Close();
            window.Close();
            app.MainWindow = previousMain;
            themes.Apply(AppTheme.Light);
            strings.SetCulture("en");
            bindingSource.Listeners.Remove(listener);
            bindingSource.Switch.Level = previousLevel;
        }
    }

    private static void CheckFileContextMenu(Application app, LocalizationService strings)
    {
        var directory = Path.Combine(Path.GetTempPath(), "FileScope-shell-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(directory, "nested"));
        var firstPath = Path.Combine(directory, "first.txt");
        var secondPath = Path.Combine(directory, "nested", "second, 中文 & #.txt");
        File.WriteAllText(firstPath, "abc");
        File.WriteAllText(secondPath, "abc");
        strings.SetCulture("en");
        var shell = new RecordingFileShell();
        var themes = new ThemeService(app);
        using var vm = CreateMainViewModel(new ImmediateAnalysis(), new EmptyPicker(), strings,
            CreateOptions(strings), fileShell: shell);
        var window = new MainWindow(vm) { ShowActivated = false, ShowInTaskbar = false };
        var errors = new StringWriter();
        using var listener = new TextWriterTraceListener(errors);
        var bindingSource = PresentationTraceSources.DataBindingSource;
        var previousLevel = bindingSource.Switch.Level;
        bindingSource.Switch.Level = SourceLevels.Error;
        bindingSource.Listeners.Add(listener);
        ContextMenu? menu = null;
        try
        {
            window.Show();
            vm.DropFilesCommand.Execute(new[] { firstPath, secondPath });
            PumpUntil(() => !vm.IsBusy && vm.Rows.Count == 2);
            var first = vm.Rows[0];
            var second = vm.Rows[1];
            var grid = (DataGrid)window.FindName("ResultsGrid");
            var errorText = (TextBlock)window.FindName("FileActionErrorText");
            var status = vm.StatusText;
            var summary = vm.LastRunSummary;
            Check(grid.ContextMenu == null, "empty grid space and column headers have no file context menu");
            Check(!vm.ShowFilePropertiesCommand.CanExecute(null) && !vm.ShowInExplorerCommand.CanExecute("wrong type"),
                "file commands are disabled without a selected file");
            vm.ShowFilePropertiesCommand.Execute(null);
            vm.ShowInExplorerCommand.Execute(null);
            Check(shell.Requests.Count == 0, "an empty selection never calls the shell service");

            vm.SelectedRow = first;
            menu = OpenMenu(second);
            var properties = MenuFor(menu, vm.ShowFilePropertiesCommand);
            var explorer = MenuFor(menu, vm.ShowInExplorerCommand);
            Check(menu.Items.Count == 4 && ReferenceEquals(properties.Command, vm.ShowFilePropertiesCommand)
                && ReferenceEquals(explorer.Command, vm.ShowInExplorerCommand), "row menu binds both file commands");
            Check(ReferenceEquals(vm.SingleSelectedRow, second) && ReferenceEquals(menu.DataContext, vm)
                && properties.CommandParameter is null && explorer.CommandParameter is null,
                "shell commands target the single selected file without retaining a row container");
            InvokeMenuItem(properties);
            PumpUntil(() => shell.Requests.Count == 1);
            Check(shell.Requests[0] == ("properties", secondPath), "Properties receives the exact nested Unicode file path");

            menu = OpenMenu(second);
            InvokeMenuItem(MenuFor(menu, vm.ShowInExplorerCommand));
            PumpUntil(() => shell.Requests.Count == 2);
            Check(shell.Requests[1] == ("explorer", secondPath), "Explorer receives the file itself, including spaces, commas and Unicode");
            Check(vm.StatusText == status && ReferenceEquals(vm.LastRunSummary, summary) && !vm.HasFileActionError,
                "successful shell actions preserve analysis status and run statistics");

            grid.SelectedItems.Add(first);
            menu = OpenMenu(first, preserveSelection: true);
            Check(!MenuFor(menu, vm.ShowFilePropertiesCommand).IsEnabled && !MenuFor(menu, vm.ShowInExplorerCommand).IsEnabled,
                "both shell menu items are disabled while multiple files are marked");
            vm.ShowFilePropertiesCommand.Execute(null);
            vm.ShowInExplorerCommand.Execute(null);
            Check(shell.Requests.Count == 2, "multi-selection cannot invoke either shell action programmatically");
            menu = OpenMenu(first);
            InvokeMenuItem(MenuFor(menu, vm.ShowInExplorerCommand));
            PumpUntil(() => shell.Requests.Count == 3);
            Check(shell.Requests[2] == ("explorer", firstPath), "selecting one different file re-enables Explorer with that file's path");

            menu = OpenMenu(second);
            shell.Succeed = false;
            vm.ShowFilePropertiesCommand.Execute(null);
            DrainDispatcher();
            Check(vm.HasFileActionError && errorText.IsVisible && errorText.Text.Contains(secondPath, StringComparison.Ordinal),
                "a failed Properties request displays a separate visible error with the file path");
            Check(vm.StatusText == status && ReferenceEquals(vm.LastRunSummary, summary) && vm.LastError.Length == 0,
                "shell failures do not replace the analysis result, timing or analysis error");
            foreach (var culture in new[] { "de", "en", "zh-Hans" })
            {
                strings.SetCulture(culture);
                menu = OpenMenu(second);
                Check((string)MenuFor(menu, vm.ShowFilePropertiesCommand).Header == strings["FilePropertiesMenu"]
                    && (string)MenuFor(menu, vm.ShowInExplorerCommand).Header == strings["ShowInExplorerMenu"],
                    "both context menu labels follow the selected language / " + culture);
                Check(errorText.Text == strings.Format("FilePropertiesError", secondPath),
                    "an existing shell error follows the selected language / " + culture);
                menu.IsOpen = false;
            }

            vm.ShowInExplorerCommand.Execute(null);
            DrainDispatcher();
            Check(errorText.Text == strings.Format("FileExplorerError", secondPath), "Explorer failures use their own localized explanation");
            shell.Succeed = true;
            vm.ShowInExplorerCommand.Execute(null);
            DrainDispatcher();
            Check(!vm.HasFileActionError && errorText.Visibility == Visibility.Collapsed, "a successful retry clears the shell error");

            foreach (var theme in new[] { AppTheme.Dark, AppTheme.Light })
            {
                themes.Apply(theme);
                menu = OpenMenu(second);
                Check(menu.Template != null && MenuFor(menu, vm.ShowFilePropertiesCommand).Template != null
                    && ReferenceEquals(vm.SingleSelectedRow, second)
                    && MenuFor(menu, vm.ShowInExplorerCommand).IsEnabled && HasNativeTheme(app, theme),
                    "native context menu templates and row commands remain usable after a theme switch / " + theme);
                menu.IsOpen = false;
            }

            shell.ExplorerCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var explorerFinished = false;
            vm.ShowInExplorerCommand.CanExecuteChanged += (_, _) =>
            {
                if (shell.ExplorerCompletion.Task.IsCompleted) explorerFinished = true;
            };
            vm.ShowInExplorerCommand.Execute(null);
            Check(!vm.ShowInExplorerCommand.CanExecute(first) && vm.ClearCommand.CanExecute(null),
                "pending Explorer requests prevent duplicate launches without blocking the rest of the window");
            vm.ClearCommand.Execute(null);
            shell.ExplorerCompletion.SetResult(false);
            PumpUntil(() => explorerFinished);
            Check(vm.Rows.Count == 0 && !vm.HasFileActionError && vm.StatusText == strings["Cleared"],
                "an Explorer completion after Clear cannot restore stale errors or rows");
            Check(!vm.ShowFilePropertiesCommand.CanExecute(second) && !vm.ShowInExplorerCommand.CanExecute(second),
                "commands reject a former row after Clear");

            // Exercise only the guards of the real service: no Explorer or property sheet is opened.
            File.Delete(secondPath);
            var native = new FileShellService();
            Check(!native.TryShowProperties(secondPath), "real Properties service rejects a file deleted since analysis");
            var missing = native.TryShowInExplorerAsync(secondPath);
            PumpUntil(() => missing.IsCompleted);
            Check(!missing.GetAwaiter().GetResult(), "real Explorer service rejects a file deleted since analysis");
            Check(errors.ToString().Length == 0, "row context menus, themes and localized errors produce no WPF binding errors: " + errors);

            ContextMenu OpenMenu(FileRowViewModel row, bool preserveSelection = false)
            {
                if (menu != null) menu.IsOpen = false;
                if (!preserveSelection)
                {
                    grid.UnselectAll();
                    grid.SelectedItems.Add(row);
                }
                grid.ScrollIntoView(row);
                window.UpdateLayout();
                DrainDispatcher();
                var container = (DataGridRow)grid.ItemContainerGenerator.ContainerFromItem(row);
                var contextMenu = container.ContextMenu;
                contextMenu.PlacementTarget = container;
                contextMenu.IsOpen = true;
                DrainDispatcher();
                return contextMenu;
            }
        }
        finally
        {
            if (menu != null) menu.IsOpen = false;
            window.Close();
            themes.Apply(AppTheme.Light);
            strings.SetCulture("en");
            bindingSource.Listeners.Remove(listener);
            bindingSource.Switch.Level = previousLevel;
            Directory.Delete(directory, true);
        }
    }

    private static MenuItem MenuFor(ContextMenu menu, System.Windows.Input.ICommand command) =>
        menu.Items.OfType<MenuItem>().Single(item => ReferenceEquals(item.Command, command));

    private static void InvokeMenuItem(MenuItem item) =>
        ((IInvokeProvider)new MenuItemAutomationPeer(item).GetPattern(PatternInterface.Invoke)).Invoke();

    private sealed class RecordingFileShell : IFileShellService
    {
        public List<(string Action, string Path)> Requests { get; } = [];
        public bool Succeed { get; set; } = true;
        public TaskCompletionSource<bool>? ExplorerCompletion { get; set; }
        public bool TryShowProperties(string fullPath) { Requests.Add(("properties", fullPath)); return Succeed; }
        public Task<bool> TryShowInExplorerAsync(string fullPath)
        {
            Requests.Add(("explorer", fullPath));
            return ExplorerCompletion?.Task ?? Task.FromResult(Succeed);
        }
    }

    private static void CheckAboutDialog(Application app, LocalizationService strings)
    {
        strings.SetCulture("en");
        var themes = new ThemeService(app);
        var store = new MemoryPreferences();
        var options = new OptionsViewModel(strings, store, themes);
        using var vm = CreateMainViewModel(new ImmediateAnalysis(), new EmptyPicker(), strings, options);
        var main = new MainWindow(vm) { ShowActivated = false, ShowInTaskbar = false };
        var links = new RecordingExternalLinks();
        var about = new AboutWindow(links) { ShowActivated = false };
        var errors = new StringWriter();
        using var listener = new TextWriterTraceListener(errors);
        var bindingSource = PresentationTraceSources.DataBindingSource;
        var previousLevel = bindingSource.Switch.Level;
        bindingSource.Switch.Level = SourceLevels.Error;
        bindingSource.Listeners.Add(listener);
        try
        {
            main.Show();
            about.Owner = main;
            var opened = false;
            app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
            {
                // Exercise the real button and its modal event handler, then close
                // the dialog without following a link or starting an external process.
                var dialog = app.Windows.OfType<AboutWindow>().SingleOrDefault(window => window.IsVisible);
                if (dialog == null) return;
                opened = ReferenceEquals(dialog.Owner, main) && ComponentDispatcher.IsThreadModal && !dialog.ShowInTaskbar;
                ((Button)dialog.FindName("CloseAboutButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }));
            var button = (Button)main.FindName("AboutButton");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(opened && !app.Windows.OfType<AboutWindow>().Any(window => window.IsVisible),
                "the real About button opens an owned modal dialog and its Close button returns to the main window");

            var saves = store.SaveCount;
            about.Show();
            DrainDispatcher();
            var assembly = typeof(App).Assembly;
            var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
            Check(((TextBlock)about.FindName("VersionValue")).Text == version,
                "About shows the running application version, including release/build suffixes, instead of the test host version");
            var license = (TextBox)about.FindName("LicenseText");
            Check(license.IsReadOnly && license.Text.StartsWith("MIT License", StringComparison.Ordinal)
                && license.Text.Contains("Copyright (c) 2026 ixylon88", StringComparison.Ordinal)
                && license.Text.Contains("THE SOFTWARE IS PROVIDED", StringComparison.Ordinal) && links.Requests.Count == 0,
                "the full MIT license and copyright are available offline without opening a browser");
            var close = (Button)about.FindName("CloseAboutButton");
            Check(close.IsCancel && close.IsDefault && !about.ShowInTaskbar,
                "About supports Escape, the default close action and no separate taskbar entry");
            var repository = (Hyperlink)about.FindName("RepositoryLink");
            var licenseLink = (Hyperlink)about.FindName("LicenseLink");
            repository.RaiseEvent(new RequestNavigateEventArgs(repository.NavigateUri, "") { RoutedEvent = Hyperlink.RequestNavigateEvent });
            Check(links.Requests.Count == 1 && links.Requests[0].AbsoluteUri == "https://github.com/ixylon88/FileScope",
                "the repository hyperlink delegates the correct HTTPS target to the replaceable link service");
            licenseLink.RaiseEvent(new RequestNavigateEventArgs(licenseLink.NavigateUri, "") { RoutedEvent = Hyperlink.RequestNavigateEvent });
            Check(links.Requests.Count == 2 && links.Requests[1].AbsoluteUri == "https://github.com/ixylon88/FileScope/blob/dev/LICENSE",
                "the online license hyperlink points to this repository's license");
            links.Succeed = false;
            repository.RaiseEvent(new RequestNavigateEventArgs(repository.NavigateUri, "") { RoutedEvent = Hyperlink.RequestNavigateEvent });
            var linkError = (TextBlock)about.FindName("LinkErrorText");
            Check(linkError.Visibility == Visibility.Visible && linkError.Text == strings["AboutLinkError"] && about.IsVisible,
                "a failed browser launch is explained inside About without closing the dialog");
            foreach (var language in strings.Languages)
            {
                strings.SetCulture(language.CultureName);
                DrainDispatcher();
                var expectedTitle = language.CultureName switch { "de" => "Über FileScope", "zh-Hans" => "关于 FileScope", _ => "About FileScope" };
                Check(about.Title == expectedTitle && string.Equals(about.Language.IetfLanguageTag, strings.Culture.IetfLanguageTag, StringComparison.OrdinalIgnoreCase)
                    && ((TextBlock)about.FindName("AboutDescription")).Text == strings["AboutDescription"]
                    && System.Windows.Automation.AutomationProperties.GetName(button) == expectedTitle,
                    "About title, description, language and accessible button name are localized / " + language.CultureName);
                var featureKeys = new[] { "AboutFeatureHashes", "AboutFeatureSignatures", "AboutFeatureMetadata", "AboutFeatureFiles", "AboutFeatureExport" };
                var features = ((StackPanel)about.FindName("FeatureList")).Children.OfType<TextBlock>().ToArray();
                Check(features.Length == featureKeys.Length && features.Zip(featureKeys).All(pair =>
                        new TextRange(pair.First.ContentStart, pair.First.ContentEnd).Text.Trim() == "• " + strings[pair.Second])
                    && ((TextBlock)about.FindName("EfficiencyText")).Text == strings["AboutEfficiency"]
                    && ((TextBlock)about.FindName("AdditionalReadsText")).Text == strings["AboutAdditionalReads"]
                    && linkError.Text == strings["AboutLinkError"],
                    "About features, read-pass qualification and browser error follow the language / " + language.CultureName);
            }
            links.Succeed = true;
            repository.RaiseEvent(new RequestNavigateEventArgs(repository.NavigateUri, "") { RoutedEvent = Hyperlink.RequestNavigateEvent });
            Check(linkError.Visibility == Visibility.Collapsed, "a successful link retry clears the previous browser error");

            foreach (var theme in new[] { AppTheme.Dark, AppTheme.Light })
            {
                themes.Apply(theme);
                DrainDispatcher();
                about.UpdateLayout();
                var expected = theme == AppTheme.Dark ? Color.FromRgb(0xB1, 0xB1, 0xBA) : Color.FromRgb(0x59, 0x65, 0x79);
                Check(((SolidColorBrush)((TextBlock)about.FindName("VersionValue")).Foreground).Color == expected
                    && ((SolidColorBrush)((TextBlock)about.FindName("AdditionalReadsText")).Foreground).Color == expected,
                    "the existing About dialog follows the application palette / " + theme);
            }
            ((Expander)about.FindName("LicenseExpander")).IsExpanded = true;
            about.Width = about.MinWidth;
            about.Height = about.MinHeight;
            DrainDispatcher();
            about.UpdateLayout();
            var scroll = (ScrollViewer)about.FindName("AboutScrollViewer");
            Check(scroll.ScrollableHeight > 0 && close.IsVisible
                && close.TranslatePoint(new Point(0, close.ActualHeight), about).Y <= about.ActualHeight,
                "About content scrolls at the minimum size while Close stays in the window");
            close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            DrainDispatcher();
            Check(!about.IsVisible && store.SaveCount == saves,
                "closing About leaves saved preferences unchanged");
            listener.Flush();
            Check(errors.ToString().Length == 0, "About produces no WPF binding errors while opening, translating and switching themes");
        }
        finally
        {
            about.Close();
            main.Close();
            bindingSource.Listeners.Remove(listener);
            bindingSource.Switch.Level = previousLevel;
            themes.Apply(AppTheme.Light);
            strings.SetCulture("en");
        }
    }

    private sealed class RecordingExternalLinks : IExternalLinkService
    {
        public List<Uri> Requests { get; } = [];
        public bool Succeed { get; set; } = true;
        public bool TryOpen(Uri uri) { Requests.Add(uri); return Succeed; }
    }

    private static ResourceDictionary[] ThemePalettes(Application app) => app.Resources.MergedDictionaries
        .Where(dictionary => dictionary.Source?.OriginalString.EndsWith("ThemePalette.xaml", StringComparison.Ordinal) == true)
        .ToArray();

#pragma warning disable WPF0001 // Exercise the native Fluent API used by ThemeService.
    private static bool HasNativeTheme(Application app, AppTheme theme) =>
        app.ThemeMode == (theme == AppTheme.Dark ? ThemeMode.Dark : ThemeMode.Light);
    private static void SetNativeTheme(Application app, AppTheme theme) =>
        app.ThemeMode = theme == AppTheme.Dark ? ThemeMode.Dark : ThemeMode.Light;
#pragma warning restore WPF0001

    private static void CheckThemeService(Application app)
    {
        var controls = app.Resources.MergedDictionaries.Single(dictionary =>
            dictionary.Source?.OriginalString.EndsWith("Controls.xaml", StringComparison.Ordinal) == true);
        var converter = app.Resources["BoolToVisibility"];
        var originalPalette = ThemePalettes(app).Single();
        var originalBrush = app.FindResource("MutedBrush");
        _ = new ThemeService(app);
        Check(ReferenceEquals(originalPalette, ThemePalettes(app).Single()) && ReferenceEquals(originalBrush, app.FindResource("MutedBrush")),
            "constructing with the existing matching App.xaml palette preserves its resources");
        foreach (var palette in ThemePalettes(app)) app.Resources.MergedDictionaries.Remove(palette);
        var service = new ThemeService(app);
        Check(service.CurrentTheme == AppTheme.Light && HasNativeTheme(app, AppTheme.Light)
            && ThemePalettes(app).Length == 1 && ((SolidColorBrush)app.FindResource("MutedBrush")).Color == Color.FromRgb(0x59, 0x65, 0x79),
            "constructing the theme service installs a missing palette even when the native mode is unchanged");
        try
        {
            service.Apply(AppTheme.Dark);
            Check(service.CurrentTheme == AppTheme.Dark && HasNativeTheme(app, AppTheme.Dark)
                && ThemePalettes(app).Length == 1 && ((SolidColorBrush)app.FindResource("MutedBrush")).Color == Color.FromRgb(0xB1, 0xB1, 0xBA),
                "applying dark changes both the custom palette and native Fluent mode");
            var palette = ThemePalettes(app).Single();
            var brush = app.FindResource("MutedBrush");
            service.Apply(AppTheme.Dark);
            Check(ReferenceEquals(palette, ThemePalettes(app).Single()) && ReferenceEquals(brush, app.FindResource("MutedBrush")),
                "reapplying an initialized theme retains the existing resource objects");
            app.Resources.MergedDictionaries.Remove(palette);
            service.Apply(AppTheme.Light);
            Check(service.CurrentTheme == AppTheme.Light && HasNativeTheme(app, AppTheme.Light)
                && ThemePalettes(app).Length == 1 && ((SolidColorBrush)app.FindResource("MutedBrush")).Color == Color.FromRgb(0x59, 0x65, 0x79),
                "inserting a palette also synchronizes native mode when no earlier palette exists");
            SetNativeTheme(app, AppTheme.Dark);
            service = new ThemeService(app);
            Check(service.CurrentTheme == AppTheme.Dark && HasNativeTheme(app, AppTheme.Dark)
                && ThemePalettes(app).Length == 1 && ((SolidColorBrush)app.FindResource("MutedBrush")).Color == Color.FromRgb(0xB1, 0xB1, 0xBA),
                "construction repairs a palette that does not match the native application mode");
            palette = ThemePalettes(app).Single();
            var rejected = false;
            try { service.Apply((AppTheme)999); }
            catch (ArgumentOutOfRangeException) { rejected = true; }
            Check(rejected && service.CurrentTheme == AppTheme.Dark && HasNativeTheme(app, AppTheme.Dark)
                && ReferenceEquals(palette, ThemePalettes(app).Single()), "invalid themes leave native mode and resources intact");
            var wrongThread = Task.Run(() =>
            {
                try { service.Apply(AppTheme.Dark); return false; }
                catch (InvalidOperationException) { return true; }
            }).GetAwaiter().GetResult();
            Check(wrongThread && ReferenceEquals(palette, ThemePalettes(app).Single()),
                "even an unchanged theme must be applied on the owning WPF dispatcher");
            Check(app.Resources.MergedDictionaries.Contains(controls) && ReferenceEquals(converter, app.Resources["BoolToVisibility"]),
                "theme changes preserve unrelated application styles and converters");
        }
        finally { service.Apply(AppTheme.Light); }
    }

    private static void CheckThemeSwitching(Application app, LocalizationService strings)
    {
        strings.SetCulture("en");
        var themes = new ThemeService(app);
        var store = new MemoryPreferences();
        var options = new OptionsViewModel(strings, store, themes);
        var analysis = new ImmediateAnalysis();
        using var vm = CreateMainViewModel(analysis, new EmptyPicker(), strings, options);
        var main = new MainWindow(vm) { ShowActivated = false, ShowInTaskbar = false };
        var settings = new OptionsWindow(options) { ShowActivated = false, ShowInTaskbar = false };
        var errors = new StringWriter();
        using var listener = new TextWriterTraceListener(errors);
        var bindingSource = PresentationTraceSources.DataBindingSource;
        var previousLevel = bindingSource.Switch.Level;
        bindingSource.Switch.Level = SourceLevels.Error;
        bindingSource.Listeners.Add(listener);
        try
        {
            main.Show();
            settings.Show();
            var input = Path.Combine(Path.GetTempPath(), "FileScope-theme-row.bin");
            vm.DropFilesCommand.Execute(new[] { input });
            PumpUntil(() => !vm.IsBusy);
            vm.SearchText = Path.GetFileName(input);
            vm.SelectedRow = vm.Rows.Single();
            options.SelectedColumn = options.Columns.Single(column => column.Id == "SHA256");
            options.MoveLastCommand.Execute(null);
            main.UpdateLayout();
            settings.UpdateLayout();
            DrainDispatcher();
            var grid = (DataGrid)main.FindName("ResultsGrid");
            var statusColumn = grid.Columns.Single(column => ColumnLayoutBehavior.GetId(column) == "Status");
            var selector = (ComboBox)settings.FindName("ThemeSelector");
            var columns = (ListBox)settings.FindName("ColumnList");
            var row = vm.Rows.Single();
            var result = row.Result;
            var digest = row.SHA256;
            var calls = analysis.Calls;
            var order = grid.Columns.OrderBy(column => column.DisplayIndex).Select(ColumnLayoutBehavior.GetId).ToArray();
            var subtitle = VisualChildren(main).OfType<TextBlock>().Single(text => text.Text == strings["Subtitle"]);
            var hint = VisualChildren(settings).OfType<TextBlock>().Single(text => text.Text == strings["HashFormatHint"]);
            Check(selector.Items.Count == 2 && ReferenceEquals(selector.SelectedItem, options.SelectedTheme)
                && options.SelectedTheme.Value == AppTheme.Light, "the real theme selector reflects the initial application appearance");
            foreach (var theme in new[] { AppTheme.Dark, AppTheme.Light, AppTheme.Dark })
            {
                var saves = store.SaveCount;
                selector.SelectedItem = options.Themes.Single(option => option.Value == theme);
                DrainDispatcher();
                grid.ScrollIntoView(row, statusColumn);
                columns.ScrollIntoView(options.SelectedColumn);
                main.UpdateLayout();
                settings.UpdateLayout();
                DrainDispatcher();
                var expectedMuted = theme == AppTheme.Dark ? Color.FromRgb(0xB1, 0xB1, 0xBA) : Color.FromRgb(0x59, 0x65, 0x79);
                var expectedBadge = theme == AppTheme.Dark ? Color.FromRgb(0xAD, 0xFE, 0xC8) : Color.FromRgb(0xDD, 0xEF, 0xE3);
                Check(options.SelectedTheme.Value == theme && themes.CurrentTheme == theme && HasNativeTheme(app, theme)
                    && store.Saved.Theme == theme.ToString() && store.SaveCount == saves + 1,
                    "selecting a theme in the real ComboBox applies and saves it immediately / " + theme);
                Check(((SolidColorBrush)subtitle.Foreground).Color == expectedMuted
                    && ((SolidColorBrush)hint.Foreground).Color == expectedMuted,
                    "existing main and options window text updates through DynamicResource / " + theme);
                // Native theme changes can rebuild DataGrid cell templates. Keep the
                // result row, but reacquire its current visual instead of inspecting
                // a detached badge whose bindings may already be disconnected.
                var statusContent = statusColumn.GetCellContent(row);
                var badge = statusContent == null ? null : VisualChildren(statusContent).OfType<StatusBadge>().SingleOrDefault();
                var actualBadge = (badge?.Content as Border)?.Background as SolidColorBrush;
                var attached = badge != null && ReferenceEquals(Window.GetWindow(badge), main);
                Check(badge is { IsLoaded: true, IsVisible: true, Tone: StatusTone.Success }
                    && attached && ReferenceEquals(badge.DataContext, row) && badge.Text == row.StateText
                    && actualBadge?.Color == expectedBadge,
                    $"the displayed success badge for the existing row follows the current palette / {theme} "
                    + $"(expected: {expectedBadge}, actual: {actualBadge?.Color.ToString() ?? "missing"}, "
                    + $"tone: {badge?.Tone.ToString() ?? "missing"}, attached: {attached})");
                Check(columns.ItemContainerGenerator.ContainerFromItem(options.SelectedColumn) is ListBoxItem item
                    && item.HorizontalContentAlignment == HorizontalAlignment.Left
                    && item.VerticalContentAlignment == VerticalAlignment.Center,
                    "realized options list items keep valid alignment after switching / " + theme);
                Check(ThemePalettes(app).Length == 1
                    && grid.Columns.OrderBy(column => column.DisplayIndex).Select(ColumnLayoutBehavior.GetId).SequenceEqual(order)
                    && grid.FrozenColumnCount == 2 && grid.EnableRowVirtualization && grid.EnableColumnVirtualization,
                    "repeated switches retain one palette, column order and grid virtualization / " + theme);
            }
            Check(analysis.Calls == calls && ReferenceEquals(row.Result, result) && row.SHA256 == digest
                && ReferenceEquals(vm.SelectedRow, row) && vm.SearchText == Path.GetFileName(input) && vm.RowsView.Count == 1,
                "theme changes preserve existing analysis results, selection and filters without another analysis");
            foreach (var language in options.Languages)
            {
                options.SelectedLanguage = language;
                DrainDispatcher();
                var expected = language.CultureName switch { "de" => "Dunkel", "zh-Hans" => "深色", _ => "Dark" };
                Check(VisualChildren(selector).OfType<TextBlock>().Any(text => text.Text == expected)
                    && System.Windows.Automation.AutomationProperties.GetName(selector) == strings["Group_Theme"]
                    && themes.CurrentTheme == AppTheme.Dark && HasNativeTheme(app, AppTheme.Dark),
                    "the displayed theme label and accessible name follow the language / " + language.CultureName);
            }
            using var reopened = new OptionsViewModel(strings, store, new ThemeService(app));
            var restoredWindow = new OptionsWindow(reopened);
            try
            {
                restoredWindow.Measure(new Size(730, 740));
                restoredWindow.Arrange(new Rect(0, 0, 730, 740));
                DrainDispatcher();
                Check(ReferenceEquals(((ComboBox)restoredWindow.FindName("ThemeSelector")).SelectedItem, reopened.SelectedTheme)
                    && reopened.SelectedTheme.Value == AppTheme.Dark && HasNativeTheme(app, AppTheme.Dark),
                    "a newly created options view and view model restore the saved theme");
            }
            finally { restoredWindow.Close(); }
            listener.Flush();
            Check(errors.ToString().Length == 0, "theme and language switches produce no WPF binding errors in existing views");
        }
        finally
        {
            settings.Close();
            main.Close();
            bindingSource.Listeners.Remove(listener);
            bindingSource.Switch.Level = previousLevel;
            themes.Apply(AppTheme.Light);
            strings.SetCulture("en");
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
                InspectCertificates = false, OnlineRevocation = true, InspectFileProperties = true, InspectPeHeader = true
            }
        });
        var saves = store.SaveCount;
        using (var vm = CreateMainViewModel(new ImmediateAnalysis(), new EmptyPicker(), strings, new OptionsViewModel(strings, store, new RecordingThemeService())))
        {
            Check(store.SaveCount == saves, "restoring checkbox state performs no writes");
            Check(vm.ChecksumOptions.Where(o => o.IsSelected).Select(o => o.Algorithm)
                .SequenceEqual(new[] { ChecksumAlgorithm.MD5, ChecksumAlgorithm.SHA1, ChecksumAlgorithm.SHA384 }),
                "main view restores each checksum independently, including disabled SHA-256");
            Check(!vm.InspectCertificates && vm.OnlineRevocation && !vm.CanChooseOnlineRevocation,
                "disabled online-revocation checkbox retains its saved selection");
            Check(vm.ShowMD5Column && vm.ShowSHA1Column && !vm.ShowSHA256Column && vm.ShowSHA384Column && !vm.ShowSHA512Column,
                "initial column visibility follows restored checksum choices");
            Check(vm.InspectFileProperties && vm.InspectPeHeader, "new metadata checkboxes restore their saved choices");
            vm.InspectFileProperties = false;
            vm.InspectPeHeader = false;
            foreach (var option in vm.ChecksumOptions) option.IsSelected = false;
            vm.OnlineRevocation = false;
            Check(store.Saved.Analysis == new AnalysisPreferences { SHA256 = false, InspectCertificates = false },
                "unchecking options persists immediately before closing the window");
        }
        var analysis = new ImmediateAnalysis();
        using var reopened = CreateMainViewModel(analysis, new EmptyPicker(), strings, new OptionsViewModel(strings, store, new RecordingThemeService()));
        Check(reopened.ChecksumOptions.All(o => !o.IsSelected) && !reopened.InspectCertificates && !reopened.OnlineRevocation && !reopened.InspectFileProperties && !reopened.InspectPeHeader,
            "restart preserves all nine checkboxes off");
        reopened.DropFilesCommand.Execute(new[] { Path.Combine(Path.GetTempPath(), "restored-options.bin") });
        PumpUntil(() => !reopened.IsBusy && reopened.Rows.Count == 1);
        Check(analysis.LastOptions is { Algorithms.Count: 0, InspectSignature: false, RevocationMode: RevocationMode.OfflineCache,
            InspectFileProperties: false, InspectPeHeader: false },
            "analysis uses restored checkbox choices");
        foreach (var option in reopened.ChecksumOptions) option.IsSelected = true;
        reopened.InspectCertificates = true;
        reopened.OnlineRevocation = true;
        reopened.InspectFileProperties = true;
        reopened.InspectPeHeader = true;
        Check(store.Saved.Analysis is { MD5: true, SHA1: true, SHA256: true, SHA384: true, SHA512: true,
            InspectCertificates: true, OnlineRevocation: true, InspectFileProperties: true, InspectPeHeader: true } && reopened.CanChooseOnlineRevocation,
            "each checksum, signature and metadata checkbox can be enabled and saved");
        reopened.ClearCommand.Execute(null);
        Check(store.Saved.Analysis == reopened.Options.AnalysisPreferences && reopened.ChecksumOptions.All(o => o.IsSelected),
            "clear keeps saved analysis choices");
    }

    private static void CheckMetadataOptions(LocalizationService strings)
    {
        var store = new MemoryPreferences();
        var analysis = new ImmediateAnalysis();
        using var vm = CreateMainViewModel(analysis, new EmptyPicker(), strings, new OptionsViewModel(strings, store, new RecordingThemeService()));
        var window = new MainWindow(vm);
        try
        {
            window.Measure(new Size(1600, 900));
            window.Arrange(new Rect(0, 0, 1600, 900));
            DrainDispatcher();
            var grid = (DataGrid)window.FindName("ResultsGrid");
            var columns = grid.Columns.ToDictionary(ColumnLayoutBehavior.GetId);
            // InitializeComponent registers names before a Window is shown. Its
            // visual tree need not exist yet, even after Measure/Arrange.
            var properties = window.FindName("InspectFilePropertiesCheckBox") as CheckBox
                ?? throw new InvalidOperationException("MainWindow is missing InspectFilePropertiesCheckBox.");
            var pe = window.FindName("InspectPeHeaderCheckBox") as CheckBox
                ?? throw new InvalidOperationException("MainWindow is missing InspectPeHeaderCheckBox.");
            Check(BindingOperations.GetBinding(properties, CheckBox.IsCheckedProperty)?.Path.Path == nameof(MainViewModel.InspectFileProperties)
                && BindingOperations.GetBinding(pe, CheckBox.IsCheckedProperty)?.Path.Path == nameof(MainViewModel.InspectPeHeader),
                "named metadata controls retain their real view-model bindings");
            string[] propertyColumns = ["FileDescription", "FileLanguage", "FileType", "FileVersion", "ProductName",
                "ProductVersion", "CompanyName", "OriginalFilename", "InternalName", "Copyright", "Trademarks", "Comments"];
            string[] peColumns = ["PeMachine", "PeFormat", "ManagedPlatform"];
            Check(properties.IsChecked == false && pe.IsChecked == false
                && propertyColumns.Concat(peColumns).All(id => columns[id].Visibility == Visibility.Collapsed),
                "metadata controls and columns start disabled for old or missing settings");
            foreach (var (inspectProperties, inspectPe) in new[] { (true, false), (false, true), (true, true), (false, false) })
            {
                properties.IsChecked = inspectProperties;
                pe.IsChecked = inspectPe;
                DrainDispatcher();
                Check(vm.InspectFileProperties == inspectProperties && vm.InspectPeHeader == inspectPe
                    && store.Saved.Analysis?.InspectFileProperties == inspectProperties && store.Saved.Analysis.InspectPeHeader == inspectPe,
                    "actual metadata checkbox bindings update and save independent choices");
                Check(propertyColumns.All(id => columns[id].Visibility == (inspectProperties ? Visibility.Visible : Visibility.Collapsed))
                    && peColumns.All(id => columns[id].Visibility == (inspectPe ? Visibility.Visible : Visibility.Collapsed)),
                    "metadata column groups follow their own checkbox");
                var calls = analysis.Calls;
                if (vm.Rows.Count == 0)
                    vm.DropFilesCommand.Execute(new[] { Path.Combine(Path.GetTempPath(), "metadata.exe") });
                else vm.ReanalyseCommand.Execute(null);
                PumpUntil(() => analysis.Calls > calls && !vm.IsBusy);
                Check(analysis.LastOptions?.InspectFileProperties == inspectProperties && analysis.LastOptions.InspectPeHeader == inspectPe
                    && (vm.Rows[0].FileDescription.Length != 0) == inspectProperties && (vm.Rows[0].PeMachine.Length != 0) == inspectPe,
                    "drop/reanalysis uses selected metadata flags and publishes the returned values");
            }
        }
        finally { window.Close(); }
    }

    private static void CheckFolderAndExport(LocalizationService strings)
    {
        strings.SetCulture("en");
        var directory = Path.Combine(Path.GetTempPath(), "FileScope-folder-ui-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(directory, "input");
        Directory.CreateDirectory(Path.Combine(source, "nested"));
        var first = Path.Combine(source, "one, 中文.bin");
        var second = Path.Combine(source, "nested", "two.bin");
        File.WriteAllText(first, "original one");
        File.WriteAllText(second, "original two");
        var picker = new TestPicker { Files = [first], Folder = source, Destination = Path.Combine(directory, "results.csv") };
        var analysis = new ImmediateAnalysis();
        using var vm = CreateMainViewModel(analysis, picker, strings, CreateOptions(strings));
        var window = new MainWindow(vm);
        var optionsWindow = new OptionsWindow(vm.Options);
        try
        {
            DrainDispatcher();
            Check(((Button)window.FindName("AddFolderButton")).Command == vm.AddFolderCommand
                && ((Button)window.FindName("ExportButton")).Command == vm.ExportCommand,
                "new ribbon buttons are bound to folder and export commands");
            Check(!vm.ExportCommand.CanExecute(null), "empty results cannot be exported");
            vm.BrowseCommand.Execute(null);
            PumpUntil(() => !vm.IsBusy && vm.Rows.Count == 1);
            vm.AddFolderCommand.Execute(null);
            PumpUntil(() => !vm.IsBusy && vm.Rows.Count == 2);
            Check(analysis.Calls == 2 && vm.Rows.All(row => row.State == RowStatus.Completed),
                "folder command includes subfolders and analyses only new paths");
            vm.AddFolderCommand.Execute(null);
            PumpUntil(() => !vm.IsBusy);
            Check(analysis.Calls == 2 && vm.Rows.Count == 2, "adding the same folder again skips all duplicates");
            vm.Options.ClearExportColumnsCommand.Execute(null);
            DrainDispatcher();
            Check(!vm.ExportCommand.CanExecute(null) && !((Button)window.FindName("ExportButton")).IsEnabled,
                "zero selected fields disables the export button");
            foreach (var column in vm.Options.Columns) column.IsExportSelected = column.Id is "Name" or "SHA256";
            var list = (ListBox)optionsWindow.FindName("ExportColumnList");
            Check(ReferenceEquals(list.ItemsSource, vm.Options.Columns), "CSV options use the complete ordered column collection");
            var sha = vm.Options.Columns.Single(column => column.Id == "SHA256");
            var checkbox = (CheckBox)list.ItemTemplate.LoadContent();
            checkbox.DataContext = sha;
            DrainDispatcher();
            checkbox.IsChecked = false;
            Check(!sha.IsExportSelected, "real CSV checkbox template updates the saved selection");
            checkbox.IsChecked = true;
            vm.Options.SetColumnOrder(["SHA256", "Name"]);
            vm.Options.SelectedHashFormat = vm.Options.HashFormats.Single(format => format.Value == HashFormat.Base64);
            vm.SearchText = "no-row-matches";
            Check(vm.RowsView.Count == 0 && vm.ExportCommand.CanExecute(null), "filters never disable export of loaded rows");
            vm.ExportCommand.Execute(null);
            PumpUntil(() => !vm.IsBusy);
            var csv = File.ReadAllText(picker.Destination!);
            var digest = Convert.ToBase64String(Convert.FromHexString(HashValue(ChecksumAlgorithm.SHA256)));
            Check(csv.StartsWith("\"SHA-256\";\"File name\"\r\n", StringComparison.Ordinal)
                && csv.Contains($"\"{digest}\";\"one, 中文.bin\"") && csv.Contains($"\"{digest}\";\"two.bin\"")
                && analysis.Calls == 2, "CSV saves every loaded row with selected fields/order/format without reanalysis");
            var separator = (ComboBox)optionsWindow.FindName("CsvDelimiterSelector");
            separator.SelectedItem = vm.Options.CsvDelimiters.Single(option => option.Value == CsvDelimiter.Comma);
            DrainDispatcher();
            vm.ExportCommand.Execute(null);
            PumpUntil(() => !vm.IsBusy);
            Check(vm.Options.SelectedCsvDelimiter.Value == CsvDelimiter.Comma
                && File.ReadAllText(picker.Destination!).StartsWith("\"SHA-256\",\"File name\"\r\n", StringComparison.Ordinal)
                && analysis.Calls == 2, "real delimiter selector changes exported separators without reanalysis");
            picker.Destination = Path.Combine(directory, "results.JSON");
            vm.ExportCommand.Execute(null);
            PumpUntil(() => !vm.IsBusy);
            using (var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(picker.Destination)))
            {
                Check(json.RootElement.GetArrayLength() == 2
                    && json.RootElement.EnumerateArray().All(row => row.GetProperty("SHA256").GetString() == digest
                        && row.EnumerateObject().Select(property => property.Name).SequenceEqual(["SHA256", "Name"]))
                    && analysis.Calls == 2,
                    "JSON command uses extension, selected stable IDs and all rows without reanalysis");
            }
            picker.Destination = Path.Combine(directory, "results.txt");
            File.WriteAllText(picker.Destination, "keep existing");
            vm.ExportCommand.Execute(null);
            PumpUntil(() => !vm.IsBusy);
            Check(vm.HasError && File.ReadAllText(picker.Destination) == "keep existing",
                "unsupported export extension reports an error without overwriting a file");
            picker.Destination = first;
            vm.ExportCommand.Execute(null);
            PumpUntil(() => !vm.IsBusy);
            Check(vm.HasError && File.ReadAllText(first) == "original one", "export cannot overwrite an analyzed source file");
            picker.Destination = null;
            vm.ExportCommand.Execute(null);
            Check(!vm.IsBusy && vm.Rows.Count == 2, "cancelling Save As leaves results unchanged");
            picker.Folder = null;
            vm.AddFolderCommand.Execute(null);
            Check(!vm.IsBusy && vm.Rows.Count == 2, "cancelling the folder picker leaves results unchanged");
            picker.Folder = Path.Combine(directory, "missing");
            vm.AddFolderCommand.Execute(null);
            PumpUntil(() => !vm.IsBusy);
            Check(vm.HasError && vm.AddFolderCommand.CanExecute(null), "folder errors are displayed and release the busy state");
        }
        finally { optionsWindow.Close(); window.Close(); Directory.Delete(directory, recursive: true); }
    }

    private static void CheckDroppedFolders(LocalizationService strings)
    {
        var directory = Path.Combine(Path.GetTempPath(), "FileScope-drop-ui-" + Guid.NewGuid().ToString("N"));
        var firstRoot = Path.Combine(directory, "first");
        var nested = Path.Combine(firstRoot, "nested");
        var secondRoot = Path.Combine(directory, "second");
        var empty = Path.Combine(directory, "empty");
        Directory.CreateDirectory(nested);
        Directory.CreateDirectory(secondRoot);
        Directory.CreateDirectory(empty);
        var existing = Path.Combine(directory, "existing.bin");
        var direct = Path.Combine(directory, "direct.bin");
        var first = Path.Combine(firstRoot, "one.bin");
        var second = Path.Combine(nested, "two.bin");
        var third = Path.Combine(secondRoot, "three.bin");
        foreach (var path in new[] { existing, direct, first, second, third }) File.WriteAllText(path, "sample");
        try
        {
            var scanner = new RecordingFolderScanner();
            var confirmation = new TestConfirmation();
            var analysis = new ImmediateAnalysis();
            var picker = new TestPicker { Files = [existing] };
            using var vm = CreateMainViewModel(analysis, picker, strings, CreateOptions(strings), scanner,
                confirmation: confirmation);
            vm.BrowseCommand.Execute(null);
            PumpUntil(() => !vm.IsBusy);
            var existingRow = vm.Rows.Single();
            confirmation.OnConfirm = () => Check(scanner.Roots.Count == 0 && analysis.Calls == 1
                && vm.Rows.Single() == existingRow, "folder confirmation precedes all scan, row and analysis work");
            vm.DropFilesCommand.Execute(new[] { firstRoot, direct });
            Check(confirmation.Calls == 1 && !vm.IsBusy && scanner.Roots.Count == 0
                && vm.Rows.Single() == existingRow && analysis.Calls == 1,
                "declining a mixed drop leaves existing rows untouched and adds neither files nor folders");
            confirmation.OnConfirm = null;
            vm.DropFilesCommand.Execute(new[] { direct });
            PumpUntil(() => !vm.IsBusy);
            Check(confirmation.Calls == 1 && analysis.Calls == 2 && vm.Rows.Count == 2,
                "file-only drops do not require confirmation");

            confirmation.Answer = true;
            vm.DropFilesCommand.Execute(new[] { firstRoot, firstRoot + Path.DirectorySeparatorChar, nested,
                secondRoot, first, direct });
            PumpUntil(() => !vm.IsBusy);
            Check(confirmation.Calls == 2 && scanner.Roots.Count == 3
                && scanner.Roots.ToHashSet().SetEquals([firstRoot, nested, secondRoot]),
                "one confirmation covers all normalized distinct folder roots");
            Check(vm.Rows.Select(row => row.FullPath).ToHashSet().SetEquals([existing, direct, first, second, third])
                && analysis.Calls == 5 && vm.Rows.All(row => row.State == RowStatus.Completed) && !vm.HasError,
                "mixed and overlapping drops recurse, deduplicate paths and create no directory error rows");
            vm.DropFilesCommand.Execute(new[] { empty });
            PumpUntil(() => !vm.IsBusy);
            Check(vm.Rows.Count == 5 && analysis.Calls == 5 && vm.StatusText == strings["FolderEmpty"],
                "an empty dropped folder adds no rows and reports an empty result");

            var remaining = Path.Combine(directory, "remaining.bin");
            File.WriteAllText(remaining, "remaining");
            confirmation.OnConfirm = () => Directory.Delete(empty);
            vm.DropFilesCommand.Execute(new[] { empty, firstRoot, remaining });
            PumpUntil(() => !vm.IsBusy);
            Check(vm.HasError && vm.LastError.Contains(empty, StringComparison.Ordinal)
                && vm.Rows.Count == 6 && analysis.Calls == 6 && vm.Rows.Any(row => row.FullPath == remaining),
                "a root lost after confirmation is reported without discarding other roots or directly dropped files");

            var blocked = new BlockingFolderScanner(observeCancellation: false);
            var cancelledAnalysis = new ImmediateAnalysis();
            using var cancelled = CreateMainViewModel(cancelledAnalysis, picker, strings, CreateOptions(strings), blocked,
                confirmation: new TestConfirmation { Answer = true });
            cancelled.BrowseCommand.Execute(null);
            PumpUntil(() => !cancelled.IsBusy);
            var retainedRow = cancelled.Rows.Single();
            cancelled.DropFilesCommand.Execute(new[] { firstRoot, direct });
            try
            {
                PumpUntil(() => blocked.Started.Task.IsCompleted);
                Check(cancelled.IsBusy && cancelled.CancelCommand.CanExecute(null)
                    && !cancelled.ClearCommand.CanExecute(null) && !cancelled.DropFilesCommand.CanExecute(new[] { third }),
                    "a confirmed folder drop participates in the busy and cancellation workflow");
                cancelled.CancelCommand.Execute(null);
            }
            finally { blocked.Release.TrySetResult(); }
            PumpUntil(() => !cancelled.IsBusy);
            Check(cancelled.Rows.Single() == retainedRow && cancelledAnalysis.Calls == 1 && !cancelled.HasError,
                "scan cancellation discards the whole mixed drop even if the scanner returns late results");

            var previewRoots = Enumerable.Range(0, 6).Select(index => Path.Combine(directory, "preview-" + index)).ToArray();
            foreach (var root in previewRoots) Directory.CreateDirectory(root);
            var localizedConfirmation = new TestConfirmation();
            using var localized = CreateMainViewModel(new ImmediateAnalysis(), new EmptyPicker(), strings, CreateOptions(strings),
                confirmation: localizedConfirmation);
            foreach (var language in localized.Options.Languages)
            {
                localized.Options.SelectedLanguage = language;
                localized.DropFilesCommand.Execute(previewRoots);
                Check(localizedConfirmation.Title == strings["DropFoldersTitle"]
                    && !localizedConfirmation.Title.StartsWith('[')
                    && localizedConfirmation.Message.Contains(previewRoots[0], StringComparison.Ordinal)
                    && !localizedConfirmation.Message.Contains(previewRoots[5], StringComparison.Ordinal)
                    && localizedConfirmation.Message.Contains(strings.Format("DropFoldersMore", 1), StringComparison.Ordinal)
                    && !localizedConfirmation.Message.Contains("{0}", StringComparison.Ordinal)
                    && localized.Rows.Count == 0,
                    "folder confirmation is localized and bounds the path preview / " + language.CultureName);
            }
        }
        finally { strings.SetCulture("en"); Directory.Delete(directory, recursive: true); }
    }

    private static void CheckFileOperationCancellation(LocalizationService strings)
    {
        var source = Path.Combine(Path.GetTempPath(), "FileScope-operation-source.bin");
        var scanner = new BlockingFolderScanner();
        var picker = new TestPicker { Files = [source], Folder = Path.GetTempPath() };
        var analysis = new ImmediateAnalysis();
        using (var vm = CreateMainViewModel(analysis, picker, strings, CreateOptions(strings), scanner))
        {
            vm.BrowseCommand.Execute(null);
            PumpUntil(() => !vm.IsBusy && vm.Rows.Count == 1);
            var existingRow = vm.Rows[0];
            vm.AddFolderCommand.Execute(null);
            PumpUntil(() => scanner.Started.Task.IsCompleted);
            Check(vm.IsBusy && vm.IsProgressIndeterminate && vm.CancelCommand.CanExecute(null)
                && !vm.ExportCommand.CanExecute(null) && !vm.BrowseCommand.CanExecute(null)
                && !vm.ClearCommand.CanExecute(null),
                "folder scanning is cancellable and disables clear and concurrent file operations");
            vm.ClearCommand.Execute(null);
            Check(vm.IsBusy && vm.Rows.Count == 1 && ReferenceEquals(vm.Rows[0], existingRow)
                && vm.CancelCommand.CanExecute(null), "disabled clear leaves the scan and existing rows unchanged");
            vm.CancelCommand.Execute(null);
            Check(vm.IsBusy && !vm.ClearCommand.CanExecute(null), "clear waits for scan cancellation to finish");
            scanner.Release.TrySetResult();
            PumpUntil(() => !vm.IsBusy);
            Check(vm.Rows.Count == 1 && ReferenceEquals(vm.Rows[0], existingRow)
                && existingRow.State == RowStatus.Completed && analysis.Calls == 1
                && !vm.IsProgressIndeterminate && !vm.HasError && vm.ClearCommand.CanExecute(null),
                "cancelled scan adds no results, keeps the existing row and enables clear");
            vm.ClearCommand.Execute(null);
            Check(vm.Rows.Count == 0 && vm.ShowEmptyState && !vm.ClearCommand.CanExecute(null),
                "clear after scan cancellation removes existing rows");
        }
        var exporter = new BlockingExporter();
        var exportPicker = new TestPicker { Files = [source], Destination = Path.Combine(Path.GetTempPath(), "FileScope-operation.csv") };
        using (var vm = CreateMainViewModel(new ImmediateAnalysis(), exportPicker, strings, CreateOptions(strings), exporter: exporter))
        {
            vm.BrowseCommand.Execute(null);
            PumpUntil(() => !vm.IsBusy && vm.Rows.Count == 1);
            vm.ExportCommand.Execute(null);
            PumpUntil(() => exporter.Started.Task.IsCompleted);
            Check(vm.IsBusy && vm.CancelCommand.CanExecute(null) && !vm.AddFolderCommand.CanExecute(null)
                && !vm.ClearCommand.CanExecute(null),
                "CSV writing participates in the same busy/cancel workflow");
            vm.ClearCommand.Execute(null);
            Check(vm.IsBusy && vm.Rows.Count == 1 && vm.CancelCommand.CanExecute(null),
                "disabled clear neither removes rows nor cancels export");
            vm.CancelCommand.Execute(null);
            exporter.Release.TrySetResult();
            PumpUntil(() => !vm.IsBusy);
            Check(vm.Rows.Count == 1 && !vm.HasError && vm.ExportCommand.CanExecute(null),
                "cancelled export retains all results and can be retried");
        }
        var intakeAnalysis = new ImmediateAnalysis();
        using var intake = CreateMainViewModel(intakeAnalysis, new EmptyPicker(), strings, CreateOptions(strings));
        intake.DropFilesCommand.Execute(Enumerable.Range(0, 300).Select(index => source + index).ToArray());
        Check(intake.IsBusy && intake.Rows.Count is > 0 and < 300, "large intake yields while preparing rows");
        var preparedRows = intake.Rows.ToArray();
        Check(!intake.ClearCommand.CanExecute(null) && intake.CancelCommand.CanExecute(null),
            "chunked intake enables cancel and disables clear");
        intake.ClearCommand.Execute(null);
        Check(intake.IsBusy && intake.Rows.SequenceEqual(preparedRows) && intake.CancelCommand.CanExecute(null),
            "disabled clear leaves chunked intake and prepared rows unchanged");
        intake.CancelCommand.Execute(null);
        PumpUntil(() => !intake.IsBusy);
        Check(intake.Rows.SequenceEqual(preparedRows) && intake.Rows.All(row => row.State == RowStatus.Cancelled)
            && intakeAnalysis.Calls == 0 && !intake.HasError && intake.ClearCommand.CanExecute(null),
            "cancelled intake keeps prepared rows, adds no further rows and starts no analysis");
        intake.ClearCommand.Execute(null);
        Check(intake.Rows.Count == 0 && !intake.HasError, "clear after intake cancellation leaves no queued rows");
    }

    private sealed class BlockingFolderScanner(bool observeCancellation = true) : IFolderScanner
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public FolderScanResult Scan(string folderPath, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            Release.Task.GetAwaiter().GetResult();
            if (observeCancellation) cancellationToken.ThrowIfCancellationRequested();
            return new FolderScanResult([Path.Combine(folderPath, "new.bin")], [], 0);
        }
    }

    private sealed class BlockingExporter : IResultExporter
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task ExportAsync(string path, ResultExportTable table, ResultExportOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Release.Task;
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    private static void CheckFileIntake(LocalizationService strings)
    {
        var picker = new TestPicker();
        using var vm = CreateMainViewModel(new ImmediateAnalysis(), picker, strings, CreateOptions(strings));
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

    private static void CheckBufferedProgress(LocalizationService strings)
    {
        strings.SetCulture("en");
        var analysis = new ControlledProgressAnalysis();
        using var vm = CreateMainViewModel(analysis, new EmptyPicker(), strings, CreateOptions(strings));
        vm.OnlineRevocation = true;
        vm.SignatureFilterOption = vm.SignatureFilters.Single(option => option.Value == SignatureFilter.NoSignature);
        vm.RowsView.SortDescriptions.Add(new SortDescription(nameof(FileRowViewModel.FileName), ListSortDirection.Ascending));
        var window = new MainWindow(vm) { ShowActivated = false, ShowInTaskbar = false };
        var wrongThread = false;
        var notifications = 0;
        var ownerThread = Environment.CurrentManagedThreadId;
        vm.PropertyChanged += (_, args) =>
        {
            wrongThread |= Environment.CurrentManagedThreadId != ownerThread;
            if (args.PropertyName == nameof(MainViewModel.Progress)) notifications++;
        };
        try
        {
            window.Show();
            var grid = (DataGrid)window.FindName("ResultsGrid");
            var filters = (Grid)window.FindName("FiltersPanel");
            vm.DropFilesCommand.Execute(new[] { Path.Combine(Path.GetTempPath(), "buffer-b.bin"), Path.Combine(Path.GetTempPath(), "buffer-a.bin") });
            PumpUntil(() => analysis.Started.Task.IsCompleted && vm.Rows[0].State == RowStatus.Running);
            Check(!filters.IsEnabled && !grid.CanUserSortColumns && !vm.ResetFiltersCommand.CanExecute(null),
                "filter controls and sorting pause during analysis");
            Check(vm.StatusText.Contains(strings["OpeningFile"], StringComparison.Ordinal),
                "status identifies file opening before any checksum or inspection begins");
            var resets = 0;
            ((INotifyCollectionChanged)vm.RowsView).CollectionChanged += (_, args) =>
            {
                if (args.Action == NotifyCollectionChangedAction.Reset) resets++;
            };
            vm.SearchText = "ignored while busy";
            vm.StateFilter = vm.StateFilters.Single(option => option.Value == RowStatus.Failed);
            Check(vm.SearchText.Length == 0 && vm.StateFilter.Value == null, "programmatic filter changes cannot force mid-batch refreshes");
            for (var i = 1; i <= 20000; i++) analysis.Progress!.Report(new AnalysisProgress(AnalysisStage.Reading, i, 40000));
            PumpUntil(() => vm.Progress > 0 && vm.Rows[0].Stage == AnalysisStage.Reading);
            Check(notifications < 100 && !wrongThread && analysis.WorkerThread != ownerThread,
                "progress bursts coalesce into UI-thread updates while analysis runs off the dispatcher");
            Check(vm.StatusText.Contains(strings["Reading"], StringComparison.Ordinal) && resets == 0,
                "checksum progress updates without repeatedly rebuilding the active filtered view");
            analysis.Progress!.Report(new AnalysisProgress(AnalysisStage.ReadingMetadata, 40000, 40000));
            PumpUntil(() => vm.Rows[0].Stage == AnalysisStage.ReadingMetadata);
            Check(vm.StatusText.Contains(strings["ReadingMetadata"], StringComparison.Ordinal), "file properties have their own visible phase");
            analysis.Progress.Report(new AnalysisProgress(AnalysisStage.ReadingPeHeader, 40000, 40000));
            PumpUntil(() => vm.Rows[0].Stage == AnalysisStage.ReadingPeHeader);
            Check(vm.StatusText.Contains(strings["ReadingPeHeader"], StringComparison.Ordinal), "EXE headers have their own visible phase");
            analysis.Progress.Report(new AnalysisProgress(AnalysisStage.CheckingSignature, 40000, 40000));
            PumpUntil(() => vm.IsCheckingSignature);
            Check(vm.IsProgressIndeterminate && vm.StatusText.Contains(strings["CheckingSignatureOnline"], StringComparison.Ordinal)
                && vm.Rows[0].StateText == strings["CheckingSignatureOnline"],
                "native signature checking explicitly includes online revocation without inventing network percentage progress");
            analysis.Release.SetResult();
            PumpUntil(() => !vm.IsBusy);
            Check(vm.Rows.All(row => row.State == RowStatus.Completed) && vm.Progress == 100 && vm.CurrentFile.Length == 0,
                "batch completion flushes all results and final progress immediately");
            Check(resets == 1 && vm.RowsView.Count == 2 && ReferenceEquals(vm.RowsView.GetItemAt(0), vm.Rows[1])
                && filters.IsEnabled && grid.CanUserSortColumns,
                "completion applies the existing filter and sort once and restores the controls");
            var status = vm.StatusText;
            analysis.Progress.Report(new AnalysisProgress(AnalysisStage.Reading, 1, 40000));
            DrainDispatcher();
            Check(vm.Progress == 100 && vm.StatusText == status && !vm.IsProgressIndeterminate,
                "late reports cannot revive a finished batch or its indeterminate indicator");
        }
        finally { analysis.Release.TrySetResult(); window.Close(); }
    }

    private sealed class ControlledProgressAnalysis : IFileAnalysisService
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IProgress<AnalysisProgress>? Progress { get; private set; }
        public int WorkerThread { get; private set; }
        public async Task<FileAnalysisResult> AnalyzeAsync(string path, AnalysisOptions options,
            IProgress<AnalysisProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            if (Progress == null)
            {
                Progress = progress;
                WorkerThread = Environment.CurrentManagedThreadId;
                Started.SetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            return Result(path, options);
        }
    }

    private static void CheckRunSummaries(LocalizationService strings)
    {
        using var vm = CreateMainViewModel(new DiagnosticAnalysis(), new EmptyPicker(), strings, CreateOptions(strings));
        var root = Path.Combine(Path.GetTempPath(), "FileScope-summary-" + Guid.NewGuid().ToString("N"));
        string Input(string name) => Path.Combine(root, name + ".bin");
        Check(vm.LastRunSummary is null, "no run summary is shown before analysis");
        vm.DropFilesCommand.Execute(new[] { Input("clean"), Input("properties"), Input("failed") });
        PumpUntil(() => !vm.IsBusy);
        Check(vm.LastRunSummary is { ProcessedFiles: 3, TotalBytes: 6, UnknownSizeFiles: 1 }
            && vm.LastRunSummary.Elapsed >= TimeSpan.Zero, "finished runs count their results and known sizes, including metadata warnings");
        Check(vm.StatusText.StartsWith(strings.Format("CompletedSummary", 3, 3, 0), StringComparison.Ordinal)
            && vm.StatusText.Contains(" · " + vm.LastRunSummary!.Format(strings), StringComparison.Ordinal)
            && vm.StatusText.EndsWith(" · " + strings.Format("DiagnosticSummary", 2), StringComparison.Ordinal),
            "completion status combines file count, duration, byte total and diagnostic details");
        vm.DropFilesCommand.Execute(new[] { Input("clean"), Input("another") });
        PumpUntil(() => !vm.IsBusy);
        Check(vm.LastRunSummary is { ProcessedFiles: 1, TotalBytes: 3, UnknownSizeFiles: 0 } && vm.Rows.Count == 4,
            "a later batch counts only newly analyzed files and excludes duplicate paths");
        vm.SearchText = Input("clean");
        vm.ReanalyseCommand.Execute(null);
        PumpUntil(() => !vm.IsBusy);
        Check(vm.LastRunSummary is { ProcessedFiles: 4, TotalBytes: 9, UnknownSizeFiles: 1 },
            "reanalysis summarizes all loaded rows, ignores the view filter and resets earlier totals");
        var previous = vm.LastRunSummary;
        vm.DropFilesCommand.Execute(new[] { Input("clean") });
        PumpUntil(() => !vm.IsBusy);
        Check(ReferenceEquals(vm.LastRunSummary, previous) && vm.StatusText.StartsWith(strings.Format("DuplicatesSkipped", 4, 4, 1), StringComparison.Ordinal),
            "a duplicate-only intake does not manufacture another analysis run");
        vm.ClearCommand.Execute(null);
        Check(vm.LastRunSummary is null && vm.StatusText == strings["Cleared"], "clear removes the previous run summary");

        var analysis = new SummaryCancellationAnalysis();
        using var cancelled = CreateMainViewModel(analysis, new EmptyPicker(), strings, CreateOptions(strings));
        cancelled.DropFilesCommand.Execute(new[] { Input("first"), Input("blocked"), Input("not-started") });
        try
        {
            PumpUntil(() => analysis.Started.Task.IsCompleted);
            Check(cancelled.LastRunSummary is null && cancelled.IsBusy, "running analysis has no finalized summary");
            cancelled.CancelCommand.Execute(null);
        }
        finally { analysis.Release.TrySetResult(); }
        PumpUntil(() => !cancelled.IsBusy);
        Check(cancelled.LastRunSummary is { ProcessedFiles: 1, TotalBytes: 3, UnknownSizeFiles: 0 }
            && cancelled.Rows.Count(row => row.State == RowStatus.Cancelled) == 2,
            "cancellation summarizes completed results and excludes unfinished files and their bytes");
        Check(cancelled.StatusText.StartsWith(strings.Format("CancelledSummary", 1, 3, 0), StringComparison.Ordinal)
            && cancelled.StatusText.Contains(" · " + cancelled.LastRunSummary!.Format(strings), StringComparison.Ordinal),
            "cancelled status also reports elapsed time and the partial byte total");
    }

    private static void CheckDiagnostics(LocalizationService strings)
    {
        var analysis = new DiagnosticAnalysis();
        using var vm = CreateMainViewModel(analysis, new EmptyPicker(), strings, CreateOptions(strings));
        var window = new MainWindow(vm);
        try
        {
            window.Measure(new Size(1080, 680));
            window.Arrange(new Rect(0, 0, 1080, 680));
            DrainDispatcher();
            var grid = (DataGrid)window.FindName("ResultsGrid");
            var columns = grid.Columns.ToDictionary(ColumnLayoutBehavior.GetId);
            var diagnostic = columns["Diagnostic"];
            var status = (TextBlock)window.FindName("OperationStatusText");
            Check(diagnostic.DisplayIndex == columns["Status"].DisplayIndex + 1,
                "default diagnostic position follows analysis status in the real grid");
            Check(!vm.ShowDiagnosticColumn && vm.DiagnosticRowCount == 0 && diagnostic.Visibility == Visibility.Collapsed,
                "an empty table has no diagnostic column or count");
            var directory = Path.Combine(Path.GetTempPath(), "FileScope-diagnostics-" + Guid.NewGuid().ToString("N"));
            string Input(string name) => Path.Combine(directory, name + ".bin");
            vm.DropFilesCommand.Execute(new[] { Input("clean") });
            PumpUntil(() => !vm.IsBusy);
            Check(!vm.ShowDiagnosticColumn && diagnostic.Visibility == Visibility.Collapsed
                && vm.StatusText.StartsWith(strings.Format("CompletedSummary", 1, 1, 0), StringComparison.Ordinal),
                "completed results without diagnostics keep the column hidden and show completion status");

            vm.DropFilesCommand.Execute(new[] { Input("properties"), Input("header"), Input("failed"), Input("multi") });
            PumpUntil(() => !vm.IsBusy);
            var metadataRow = vm.Rows.Single(row => row.FullPath == Input("properties"));
            Check(vm.DiagnosticRowCount == 3 && vm.ShowDiagnosticColumn && diagnostic.Visibility == Visibility.Visible
                && metadataRow.State == RowStatus.Completed,
                "optional metadata diagnostics and file failures reveal the column and count affected rows");
            Check(vm.StatusText.StartsWith(strings.Format("CompletedSummary", 4, 4, 0), StringComparison.Ordinal)
                && vm.StatusText.EndsWith(" · " + strings.Format("DiagnosticSummary", 3), StringComparison.Ordinal)
                && status.Text == vm.StatusText && status.TextWrapping == TextWrapping.Wrap
                && status.TextTrimming == TextTrimming.None,
                "the bound completion status appends diagnostic details and wraps instead of truncating them");

            var multi = vm.Rows.Single(row => row.HasMultipleSignatures);
            multi.SelectedSignature = multi.Signatures[1];
            DrainDispatcher();
            Check(vm.DiagnosticRowCount == 4 && multi.Diagnostic.Contains("secondary signature detail", StringComparison.Ordinal),
                "selecting a signature with diagnostic details updates the row count");
            multi.SelectedSignature = multi.Signatures[0];
            DrainDispatcher();
            Check(vm.DiagnosticRowCount == 3, "selecting a clean signature removes only that row from the diagnostic count");
            vm.SearchText = Input("clean");
            DrainDispatcher();
            Check(vm.RowsView.Count == 1 && vm.DiagnosticRowCount == 3 && diagnostic.Visibility == Visibility.Visible,
                "filtering diagnostic rows out does not hide their column or status hint");
            vm.ResetFiltersCommand.Execute(null);
            vm.InspectCertificates = false;
            DrainDispatcher();
            Check(diagnostic.Visibility == Visibility.Visible, "diagnostic visibility is independent of enabled analysis options");
            vm.InspectCertificates = true;

            foreach (var language in vm.Options.Languages)
            {
                vm.Options.SelectedLanguage = language;
                DrainDispatcher();
                Check(status.Text == vm.StatusText && status.Text.EndsWith(" · " + strings.Format("DiagnosticSummary", 3), StringComparison.Ordinal)
                    && !status.Text.Contains("[DiagnosticSummary]", StringComparison.Ordinal),
                    "diagnostic status follows the selected language / " + language.CultureName);
            }
            vm.Options.SelectedColumn = vm.Options.Columns.Single(column => column.Id == "Diagnostic");
            vm.Options.MoveLastCommand.Execute(null);
            DrainDispatcher();
            var selectedPosition = diagnostic.DisplayIndex;
            Check(selectedPosition == grid.Columns.Count - 1, "diagnostics can still be moved to a custom position");
            analysis.IncludeDiagnostics = false;
            vm.ReanalyseCommand.Execute(null);
            PumpUntil(() => !vm.IsBusy);
            Check(vm.DiagnosticRowCount == 0 && !vm.ShowDiagnosticColumn && diagnostic.Visibility == Visibility.Collapsed
                && vm.StatusText.StartsWith(strings.Format("CompletedSummary", 5, 5, 0), StringComparison.Ordinal),
                "a clean reanalysis removes stale diagnostics, hides the column and removes the status hint");
            analysis.IncludeDiagnostics = true;
            vm.ReanalyseCommand.Execute(null);
            PumpUntil(() => !vm.IsBusy);
            Check(vm.DiagnosticRowCount == 3 && diagnostic.Visibility == Visibility.Visible
                && diagnostic.DisplayIndex == selectedPosition,
                "new diagnostics restore visibility without overwriting a custom column position");
            vm.Options.ResetOrderCommand.Execute(null);
            DrainDispatcher();
            Check(diagnostic.DisplayIndex == columns["Status"].DisplayIndex + 1,
                "reset restores the new default diagnostic position");
            vm.ClearCommand.Execute(null);
            DrainDispatcher();
            Check(vm.DiagnosticRowCount == 0 && diagnostic.Visibility == Visibility.Collapsed
                && vm.StatusText == strings["Cleared"], "clear removes both diagnostics and the status hint");
            metadataRow.Complete(Result(metadataRow.FullPath, new AnalysisOptions([ChecksumAlgorithm.SHA256], false)) with
            {
                Properties = new(null, "late metadata diagnostic")
            });
            Check(vm.DiagnosticRowCount == 0, "cleared rows no longer contribute diagnostic notifications");
            vm.DropFilesCommand.Execute(new[] { Input("failed") });
            PumpUntil(() => !vm.IsBusy);
            Check(vm.DiagnosticRowCount == 1 && diagnostic.Visibility == Visibility.Visible,
                "a new failed row after clear makes diagnostics visible again");
        }
        finally { window.Close(); strings.SetCulture("en"); }
    }

    private static void CheckMultipleSignatures(LocalizationService strings)
    {
        var analysis = new MultipleSignatureAnalysis();
        using var vm = CreateMainViewModel(analysis, new EmptyPicker(), strings, CreateOptions(strings));
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
            vm.SignatureFilterOption = vm.SignatureFilters.Single(f => f.Value == SignatureFilter.ExceptValid);
            Check(vm.RowsView.Count == 1 && vm.RowsView.Contains(row), "except-valid includes the selected untrusted signature");
            combo.SelectedItem = row.Signatures[0];
            DrainDispatcher();
            Check(vm.RowsView.Count == 0, "selecting a valid signature removes the row from except-valid");
            combo.SelectedItem = row.Signatures[1];
            DrainDispatcher();
            Check(vm.RowsView.Count == 1 && analysis.Calls == calls, "selecting a warning restores the row without reanalysis");
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

    private static void CheckExceptValidFilter(LocalizationService strings)
    {
        using var vm = CreateMainViewModel(new ImmediateAnalysis(), new EmptyPicker(), strings, CreateOptions(strings));
        foreach (var status in Enum.GetValues<SignatureStatus>())
        {
            var row = new FileRowViewModel(Path.Combine(Path.GetTempPath(), status + ".exe"), strings);
            row.Complete(new FileAnalysisResult(row.FullPath, 0, null, new Dictionary<ChecksumAlgorithm, string>(),
                new SignatureResult(status, RevocationMode.OfflineCache)));
            vm.Rows.Add(row);
        }
        vm.SignatureFilterOption = vm.SignatureFilters.Single(filter => filter.Value == SignatureFilter.ExceptValid);
        Check(vm.RowsView.Count == Enum.GetValues<SignatureStatus>().Length - 1
            && vm.RowsView.Cast<FileRowViewModel>().All(row => row.SignatureState != SignatureStatus.Valid),
            "except-valid excludes only valid and includes unsigned, unchecked, unsupported and failed signatures");
        vm.SearchText = nameof(SignatureStatus.CheckFailed);
        Check(vm.RowsView.Count == 1, "except-valid combines with text search");
        vm.SearchText = "";
        vm.SignatureFilterOption = vm.SignatureFilters.Single(filter => filter.Value == SignatureFilter.Attention);
        Check(vm.RowsView.Count == Enum.GetValues<SignatureStatus>().Length - 3,
            "existing attention filter continues to omit unsigned and unchecked files");
    }

    private static void CheckWindowPersistence(LocalizationService strings)
    {
        foreach (var maximize in new[] { false, true })
        {
            var store = new MemoryPreferences();
            var options = new OptionsViewModel(strings, store, new RecordingThemeService());
            using var vm = CreateMainViewModel(new ImmediateAnalysis(), new EmptyPicker(), strings, options);
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
                using var nextVm = CreateMainViewModel(new ImmediateAnalysis(), new EmptyPicker(), strings,
                    new OptionsViewModel(strings, store, new RecordingThemeService()));
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

    private static void CheckOptionsWindowPersistence(LocalizationService strings)
    {
        foreach (var maximize in new[] { false, true })
        foreach (var minimize in new[] { false, true })
        {
            var store = new MemoryPreferences();
            using var options = new OptionsViewModel(strings, store, new RecordingThemeService());
            var main = new WindowPreferences(1200, 800, false);
            options.SaveWindowPreferences(main);
            var window = new OptionsWindow(options) { ShowActivated = false };
            var closed = false;
            var area = SystemParameters.WorkArea;
            var width = Math.Min(900, area.Width);
            var height = Math.Min(850, area.Height);
            try
            {
                window.Show();
                DrainDispatcher();
                window.Width = width;
                window.Height = height;
                DrainDispatcher();
                if (maximize) window.WindowState = WindowState.Maximized;
                DrainDispatcher();
                if (minimize) window.WindowState = WindowState.Minimized;
                DrainDispatcher();
                window.Close();
                closed = true;
                Check(store.Saved.OptionsWindow is { } saved && saved.Maximized == maximize
                    && Math.Abs(saved.Width - width) < 2 && Math.Abs(saved.Height - height) < 2
                    && store.Saved.Window == main,
                    "options close preserves normal dimensions and independent state / " + maximize + "/" + minimize);
                using var reopened = new OptionsViewModel(strings, store, new RecordingThemeService());
                // WPF requires activation when the initial window state is maximized.
                var next = new OptionsWindow(reopened);
                try
                {
                    Check(next.WindowState == (maximize ? WindowState.Maximized : WindowState.Normal)
                        && Math.Abs(next.Width - width) < 2 && Math.Abs(next.Height - height) < 2
                        && next.WindowStartupLocation == WindowStartupLocation.CenterOwner,
                        "reopened options restore size/state and keep CenterOwner / " + maximize + "/" + minimize);
                    next.Show();
                    DrainDispatcher();
                    next.WindowState = WindowState.Normal;
                    DrainDispatcher();
                    Check(Math.Abs(next.ActualWidth - width) < 2 && Math.Abs(next.ActualHeight - height) < 2,
                        "restoring maximized options recovers the saved normal size");
                }
                finally { next.Close(); }
            }
            finally { if (!closed) window.Close(); }
        }
    }

    private static void CheckHorizontalScrolling(LocalizationService strings)
    {
        using var vm = CreateMainViewModel(new ImmediateAnalysis(), new EmptyPicker(), strings, CreateOptions(strings));
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

    private static void CheckTemplateCellBindings(LocalizationService strings)
    {
        using var vm = CreateMainViewModel(new MultipleSignatureAnalysis(), new EmptyPicker(), strings, CreateOptions(strings));
        vm.InspectCertificates = true;
        var window = new MainWindow(vm) { ShowActivated = false, ShowInTaskbar = false };
        var trace = PresentationTraceSources.DataBindingSource;
        var previousLevel = trace.Switch.Level;
        using var messages = new StringWriter();
        using var listener = new TextWriterTraceListener(messages);
        trace.Switch.Level = SourceLevels.Error;
        trace.Listeners.Add(listener);
        try
        {
            window.Show();
            var root = Path.Combine(Path.GetTempPath(), "FileScope-template-bindings");
            vm.DropFilesCommand.Execute(new[] { Path.Combine(root, "multi.exe"), Path.Combine(root, "single.exe") });
            PumpUntil(() => !vm.IsBusy && vm.Rows.Count == 2);
            var grid = (DataGrid)window.FindName("ResultsGrid");
            var first = vm.Rows.Single(row => row.FileName == "multi.exe");
            var second = vm.Rows.Single(row => row.FileName == "single.exe");
            var columns = grid.Columns.OfType<DataGridTemplateColumn>().ToArray();
            Check(columns.Length == 3 && columns.All(column => column is CellBoundTemplateColumn),
                "all three production template columns use cell-bound presenters");

            // Call the framework's generation hook without attaching its result.
            // This deliberately separates creation from attachment; a regular
            // rendered-cell test would hide the missing-parent binding failure.
            var generate = typeof(CellBoundTemplateColumn).GetMethod("GenerateElement", BindingFlags.Instance | BindingFlags.NonPublic)!;
            foreach (var column in columns)
            {
                var id = ColumnLayoutBehavior.GetId(column);
                var cell = new DataGridCell { DataContext = first };
                var presenter = (ContentPresenter)generate.Invoke(column, new object[] { cell, first })!;
                DrainDispatcher();
                var binding = BindingOperations.GetBindingExpression(presenter, ContentPresenter.ContentProperty)!;
                Check(VisualTreeHelper.GetParent(presenter) is null && ReferenceEquals(presenter.Content, first) && !binding.HasError,
                    "an unattached template presenter resolves its row without a parent / " + id);
                cell.DataContext = second;
                DrainDispatcher();
                Check(ReferenceEquals(presenter.Content, second) && !binding.HasError,
                    "the same presenter follows a recycled cell's new row / " + id);
                cell.DataContext = null;
                DrainDispatcher();
                var cleared = presenter.Content is null && !binding.HasError;
                cell.DataContext = first;
                DrainDispatcher();
                Check(cleared && ReferenceEquals(presenter.Content, first) && !binding.HasError,
                    "a null cell context clears the presenter and later recovers / " + id);
                Check(DisplaysRow(column, first), "the realized template displays the correct row / " + id);
                BindingOperations.ClearBinding(presenter, ContentPresenter.ContentProperty);
            }

            var editing = typeof(CellBoundTemplateColumn).GetMethod("GenerateEditingElement", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var editingCell = new DataGridCell { DataContext = first };
            var editingPresenter = (ContentPresenter)editing.Invoke(columns[0], new object[] { editingCell, first })!;
            DrainDispatcher();
            Check(ReferenceEquals(editingPresenter.Content, first)
                && ReferenceEquals(editingPresenter.ContentTemplate, columns[0].CellTemplate),
                "editing generation preserves the base column's fallback to the display template");
            BindingOperations.ClearBinding(editingPresenter, ContentPresenter.ContentProperty);

            vm.SearchText = "no-matching-template-row";
            DrainDispatcher();
            Check(grid.Items.Count == 0, "filtering can detach all template cells");
            vm.InspectCertificates = false;
            DrainDispatcher();
            vm.SearchText = "";
            vm.InspectCertificates = true;
            DrainDispatcher();
            Check(columns.All(column => DisplaysRow(column, second)) && columns.All(column => DisplaysRow(column, first)),
                "restored rows and signature columns display the right data after filtering and visibility changes");
            window.Close();
            DrainDispatcher();
            trace.Flush();
            Check(messages.ToString().Length == 0,
                "template creation, row reuse, filtering and window closure produce no binding errors: " + messages);

            bool DisplaysRow(DataGridTemplateColumn column, FileRowViewModel row)
            {
                grid.ScrollIntoView(row, column);
                grid.UpdateLayout();
                DrainDispatcher();
                if (column.GetCellContent(row) is not ContentPresenter content || !ReferenceEquals(content.Content, row)) return false;
                return ColumnLayoutBehavior.GetId(column) switch
                {
                    "Status" => VisualChildren(content).OfType<StatusBadge>().Any(badge =>
                        ReferenceEquals(badge.DataContext, row) && badge.Text == row.StateText),
                    "Signature" => VisualChildren(content).OfType<StatusBadge>().Any(badge =>
                        ReferenceEquals(badge.DataContext, row) && badge.Text == row.SignatureText),
                    "SignatureChoice" => VisualChildren(content).OfType<ComboBox>().Any(box =>
                        ReferenceEquals(box.ItemsSource, row.Signatures) && ReferenceEquals(box.SelectedItem, row.SelectedSignature)),
                    _ => false
                };
            }
        }
        finally
        {
            window.Close();
            trace.Listeners.Remove(listener);
            trace.Switch.Level = previousLevel;
        }
    }

    private static void CheckScrollBarSizing()
    {
        var viewer = new ScrollViewer
        {
            Content = new Border { Width = 1200, Height = 1200 },
            HorizontalScrollBarVisibility = ScrollBarVisibility.Visible,
            VerticalScrollBarVisibility = ScrollBarVisibility.Visible
        };
        var window = new Window { Content = viewer, Width = 420, Height = 300, ShowActivated = false, ShowInTaskbar = false };
        try
        {
            window.Show();
            DrainDispatcher();
            var vertical = (ScrollBar)viewer.Template.FindName("PART_VerticalScrollBar", viewer);
            var horizontal = (ScrollBar)viewer.Template.FindName("PART_HorizontalScrollBar", viewer);
            var verticalBounds = vertical.TransformToAncestor(viewer).TransformBounds(new Rect(vertical.RenderSize));
            var horizontalBounds = horizontal.TransformToAncestor(viewer).TransformBounds(new Rect(horizontal.RenderSize));
            Check(verticalBounds.Width >= 24 && horizontalBounds.Height >= 24,
                "real scrollbars reserve at least 24 DIPs of rendered width/height");
            foreach (var bar in new[] { vertical, horizontal })
            {
                var track = (Track)bar.Template.FindName("PART_Track", bar);
                var thumb = track.Thumb;
                var bounds = thumb.TransformToAncestor(viewer).TransformBounds(new Rect(thumb.RenderSize));
                var thickness = bar.Orientation == Orientation.Vertical ? bounds.Width : bounds.Height;
                Check(thickness >= 8, "native thumb has a doubled visible and clickable cross-section / " + bar.Orientation);
                var point = thumb.TranslatePoint(new Point(thumb.ActualWidth / 2, thumb.ActualHeight / 2), viewer);
                var hit = viewer.InputHitTest(point) as DependencyObject;
                while (hit is not null and not Thumb) { hit = VisualTreeHelper.GetParent(hit); }
                Check(ReferenceEquals(hit, thumb), "scaled thumb remains reachable by mouse hit-testing / " + bar.Orientation);
            }
            ScrollBar.LineDownCommand.Execute(null, vertical);
            ScrollBar.LineRightCommand.Execute(null, horizontal);
            DrainDispatcher();
            Check(viewer.VerticalOffset > 0 && viewer.HorizontalOffset > 0,
                "both native scrollbars retain functional scroll commands after scaling");
        }
        finally { window.Close(); }
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
        var columns = grid.Columns.ToDictionary(ColumnLayoutBehavior.GetId);
        var results = vm.Rows.Select(row => row.Result).ToArray();
        var analysisCalls = analysis.Calls;
        string[] certificateColumns = ["SigningTime", "Signature", "Publisher", "Subject", "Issuer", "Serial",
            "ValidFrom", "ValidUntil", "CertSha1", "CertSha256", "CertAlgorithm", "NativeCode"];
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
            string[] stringArray = ["Name", "Size", "Modified", "Path"];
            Check(stringArray.All(path => columns[path].Visibility == Visibility.Visible), "file metadata stays visible");
            Check(columns["Diagnostic"].Visibility == (vm.ShowDiagnosticColumn ? Visibility.Visible : Visibility.Collapsed),
                "diagnostic visibility depends on row content, independently of certificate options");
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
        Check(grid.AlternationCount == 0, "table keeps row striping disabled");
        Check(grid.ReadLocalValue(Control.BackgroundProperty) == DependencyProperty.UnsetValue
            && grid.ReadLocalValue(Control.ForegroundProperty) == DependencyProperty.UnsetValue,
            "table uses the application theme instead of fixed light colors");
        Check(grid.Columns.All(column => column.Width.IsAuto && column.MinWidth > 0
                && (ColumnLayoutBehavior.GetId(column) == "Path" ? double.IsPositiveInfinity(column.MaxWidth) : column.MaxWidth < 1000)),
            "automatic sizing bounds regular columns and lets the final path column fit its content");
        Check(grid.Columns.Where(column => column.Visibility == Visibility.Visible)
            .All(column => column.ActualWidth >= column.MinWidth && column.ActualWidth <= column.MaxWidth),
            "visible columns respect minimum and maximum widths");
    }

    private sealed class RecordingThemeService(AppTheme initialTheme = AppTheme.Light) : IThemeService
    {
        public AppTheme CurrentTheme { get; private set; } = initialTheme;
        public int ApplyCount { get; private set; }
        public bool FailApply { get; set; }
        public void Apply(AppTheme theme)
        {
            if (FailApply) throw new InvalidOperationException("Test theme failure");
            CurrentTheme = theme;
            ApplyCount++;
        }
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
        public string? PickFolder(string title) => null;
        public string? PickExportDestination(string title, string csvFilesLabel, string jsonFilesLabel) => null;
    }

    private static MainViewModel CreateMainViewModel(IFileAnalysisService analysis, IFilePickerService picker,
        LocalizationService strings, OptionsViewModel options, IFolderScanner? scanner = null, IResultExporter? exporter = null,
        IConfirmationService? confirmation = null, IFileShellService? fileShell = null,
        IFileMoveService? fileMover = null, IFileMoveReportService? moveReports = null) =>
        new(analysis, picker, strings, options, scanner ?? new FolderScanner(),
            exporter ?? new ResultExporter([new CsvExportStrategy(), new JsonExportStrategy()]), confirmation ?? new TestConfirmation(),
            fileShell ?? new RecordingFileShell(), fileMover ?? new RecordingFileMover(), moveReports ?? new RecordingMoveReports());

    private sealed class RecordingFileMover : IFileMoveService
    {
        public string[] Sources { get; private set; } = [];
        public string Folder { get; private set; } = "";
        public IReadOnlySet<string>? Listed { get; private set; }
        public int ThreadId { get; private set; }
        public Func<IReadOnlyList<string>, string, CancellationToken, IReadOnlyList<FileMoveResult>>? OnMove { get; set; }
        public IReadOnlyList<FileMoveResult> MoveToSubfolder(IReadOnlyList<string> sources, string folderName,
            IReadOnlySet<string> listedPaths, CancellationToken cancellationToken = default)
        {
            Sources = sources.ToArray();
            Folder = folderName;
            Listed = listedPaths;
            ThreadId = Environment.CurrentManagedThreadId;
            return OnMove?.Invoke(sources, folderName, cancellationToken)
                ?? sources.Select(path => new FileMoveResult(path, Path.Combine(Path.GetDirectoryName(path)!, folderName, Path.GetFileName(path)), FileMoveOutcome.Moved)).ToArray();
        }
    }

    private sealed class RecordingMoveReports : IFileMoveReportService
    {
        public IReadOnlyList<FileMoveResult>? Results { get; private set; }
        public int ThreadId { get; private set; }
        public void Show(IReadOnlyList<FileMoveResult> results)
        {
            Results = results;
            ThreadId = Environment.CurrentManagedThreadId;
        }
    }

    private sealed class TestConfirmation : IConfirmationService
    {
        public bool Answer { get; set; }
        public int Calls { get; private set; }
        public string Message { get; private set; } = "";
        public string Title { get; private set; } = "";
        public Action? OnConfirm { get; set; }
        public bool Confirm(string message, string title)
        {
            Calls++;
            Message = message;
            Title = title;
            OnConfirm?.Invoke();
            return Answer;
        }
    }

    private sealed class RecordingFolderScanner : IFolderScanner
    {
        public List<string> Roots { get; } = [];
        public FolderScanResult Scan(string folderPath, CancellationToken cancellationToken = default)
        {
            Roots.Add(folderPath);
            return new FolderScanner().Scan(folderPath, cancellationToken);
        }
    }

    private static OptionsViewModel CreateOptions(LocalizationService strings) => new(strings, new MemoryPreferences(), new RecordingThemeService());
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
        public string? Folder { get; set; }
        public string? Destination { get; set; }
        public string? PickFolder(string title) => Folder;
        public string? PickExportDestination(string title, string csvFilesLabel, string jsonFilesLabel) => Destination;
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
    private sealed class SummaryCancellationAnalysis : IFileAnalysisService
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<FileAnalysisResult> AnalyzeAsync(string path, AnalysisOptions options,
            IProgress<AnalysisProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            if (Path.GetFileNameWithoutExtension(path) == "blocked")
            {
                Started.TrySetResult();
                await Release.Task;
            }
            cancellationToken.ThrowIfCancellationRequested();
            return Result(path, options);
        }
    }

    private sealed class DiagnosticAnalysis : IFileAnalysisService
    {
        public bool IncludeDiagnostics { get; set; } = true;
        public Task<FileAnalysisResult> AnalyzeAsync(string path, AnalysisOptions options,
            IProgress<AnalysisProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = Result(path, options);
            if (IncludeDiagnostics)
            {
                result = Path.GetFileNameWithoutExtension(path) switch
                {
                    "properties" => result with { Properties = new(null, "Version read failed") },
                    "header" => result with { PeHeader = new(null, "Header read failed") },
                    "failed" => FileAnalysisResult.Failure(path, AnalysisError.ReadFailure, "File read failed"),
                    "multi" => result with
                    {
                        Signature = new(SignatureStatus.Valid, options.RevocationMode)
                        {
                            Signatures =
                            [
                                new(0, SignatureStatus.Valid, null, 0, null, "SHA-256", SignatureTimestamp.NotPresent),
                                new(1, SignatureStatus.Untrusted, null, 1, "secondary signature detail", "SHA-256", SignatureTimestamp.NotPresent)
                            ]
                        }
                    },
                    _ => result
                };
            }
            return Task.FromResult(result);
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
        new SignatureResult(SignatureStatus.NoEmbeddedSignature, options.RevocationMode))
    {
        Properties = options.InspectFileProperties ? new(new FilePropertiesInfo { FileDescription = "Example description", FileVersion = "1.2.3.4" }) : null,
        PeHeader = options.InspectPeHeader ? new(new PeHeaderInfo(0x014c, "i386", "PE32", ManagedPlatform.AnyCpu)) : null
    };
}
