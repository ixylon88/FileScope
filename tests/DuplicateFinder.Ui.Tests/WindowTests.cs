using System.ComponentModel;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Windows.Markup;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DuplicateFinder.App;
using DuplicateFinder.Core;
using FileScope.Core.Models;
using System.Reflection;
using FileScope.Ui.Tests;

namespace DuplicateFinder.Ui.Tests;

[TestClass]
public sealed class WindowTests
{
    private readonly string _settingsFolder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    private SettingsStore Store => new(Path.Combine(_settingsFolder, "settings.json"));

    [TestCleanup]
    public void RemoveSettings()
    {
        if (Directory.Exists(_settingsFolder)) Directory.Delete(_settingsFolder, true);
    }

    private static FoundFile FileAt(string name, int priority) => new(name, new("search", priority))
    { Group = 1, Variant = 1, Streams = StreamComparison.Identical };

    [TestMethod]
    public Task FilterBarDistinguishesOnlineAndCacheAndResetsTheView() => WpfTestHost.RunAsync((app, strings) =>
    {
        var online = FileAt("AlphaOnline.bin", 10);
        var offline = FileAt("AlphaOffline.bin", 0);
        online.Embedded = new(SignatureStatus.Valid, RevocationMode.Online);
        offline.Embedded = new(SignatureStatus.Valid, RevocationMode.OfflineCache);
        var model = new ResultsViewModel();
        model.Replace([online, offline]);
        model.Rows[0].Marked = true;
        var window = ShowResults(model, new ThemeService(app));
        var text = (TextBox)window.FindName("FilterBox");
        var kind = (ComboBox)window.FindName("KindBox");
        var signature = (ComboBox)window.FindName("SignatureBox");
        var reset = (Button)window.FindName("ResetFiltersButton");

        text.Text = "Alpha";
        kind.SelectedIndex = 1;
        signature.SelectedValue = SignatureFilter.Exact(SignatureGroup.From(SignatureStatus.Valid, RevocationMode.OfflineCache)!.Value);
        Assert.AreSame(offline, model.View.Cast<ResultRow>().Single().File);
        Assert.IsTrue(model.Rows[0].Marked);
        Assert.IsTrue(reset.Command.CanExecute(null));
        reset.Command.Execute(null);
        Assert.AreEqual("", text.Text);
        Assert.AreEqual(0, kind.SelectedIndex);
        Assert.AreEqual(SignatureFilter.All, signature.SelectedValue);
        Assert.HasCount(2, model.View.Cast<ResultRow>().ToArray());
        Assert.AreEqual(1, model.MarkedCount);

        window.Width = 720;
        window.UpdateLayout();
        var narrow = text.ActualWidth;
        window.Width = 1070;
        window.UpdateLayout();
        Assert.IsGreaterThan(narrow + 100, text.ActualWidth);
        var panel = (Grid)window.FindName("FiltersPanel");
        Assert.AreEqual(panel.ActualWidth, reset.TranslatePoint(new Point(reset.ActualWidth, 0), panel).X, 1.0);
    });

    [TestMethod]
    public Task AboutShowsDuplicateFinderBrandVersionAndOfflineLicense() => WpfTestHost.RunAsync((app, strings) =>
    {
        var owner = ShowResults(new ResultsViewModel(), new ThemeService(app));
        var about = AboutWindow.Create();
        about.Owner = owner;
        about.Show();
        Assert.IsNotNull(owner.Icon);
        Assert.IsNotNull(((Image)about.FindName("ProductIcon")).Source);
        Assert.AreEqual("DuplicateFinder", ((TextBlock)about.FindName("ProductName")).Text);
        var version = typeof(MainWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        Assert.AreEqual(version, ((TextBlock)about.FindName("VersionValue")).Text);
        StringAssert.Contains(((TextBox)about.FindName("LicenseText")).Text, "MIT License");

        foreach (var language in strings.Languages)
        {
            strings.SetCulture(language.CultureName);
            about.UpdateLayout();
            StringAssert.Contains(about.Title, "DuplicateFinder");
            Assert.AreEqual(strings["AboutTitle"], about.Title);
            var features = ((ItemsControl)about.FindName("FeatureList")).Items.Cast<string>().ToArray();
            CollectionAssert.Contains(features, strings["AboutComparison"]);
            CollectionAssert.Contains(features, strings["AboutRecycle"]);
            Assert.IsFalse(features.Any(feature => feature.StartsWith('[')));
        }
    });

    [TestMethod]
    public Task FilterScopeIncludesCollapsedRowsAndAnInvisibleKeeper() => WpfTestHost.RunAsync((app, strings) =>
    {
        var model = new ResultsViewModel();
        model.Replace([FileAt("archive", 30), FileAt("backup", 20), FileAt("download", 10)]);
        model.Filter("download", 1, SignatureFilter.All);
        var filtered = model.Plan(true);
        var all = model.Plan(false);
        Assert.AreEqual("archive", filtered.Items.Single().Keeper!.Path);
        Assert.AreEqual("download", filtered.Items.Single().File.Path);
        Assert.HasCount(2, all.Items);

        var dialog = new ScopeWindow(filtered, all);
        dialog.Show();
        Assert.IsTrue(((RadioButton)dialog.FindName("VisibleFiles")).IsChecked);
        Assert.IsTrue(((TextBlock)dialog.FindName("VisibleCount")).Text.Contains('1'));
        Assert.IsFalse(((RadioButton)dialog.FindName("AllFiles")).IsChecked);
    });

    [TestMethod]
    public Task EmptyFilterScopeRequiresExplicitChoiceToIncludeHiddenFiles() => WpfTestHost.RunAsync((app, strings) =>
    {
        var model = new ResultsViewModel();
        model.Replace([FileAt("high", 10), FileAt("low", 0)]);
        model.Filter("no match", 0, SignatureFilter.All);
        var dialog = new ScopeWindow(model.Plan(true), model.Plan(false));
        dialog.Show();
        var confirm = (Button)dialog.FindName("Confirm");
        Assert.IsFalse(confirm.IsEnabled);
        Assert.IsFalse(confirm.Command.CanExecute(null));
        ((RadioButton)dialog.FindName("AllFiles")).IsChecked = true;
        Assert.IsTrue(confirm.IsEnabled);
        Assert.IsTrue(confirm.Command.CanExecute(null));
        Assert.IsFalse(dialog.Filtered);
    });

    [TestMethod]
    public Task HiddenMarksSurviveFilterAndWarnAboutLastCopy() => WpfTestHost.RunAsync((app, strings) =>
    {
        var model = new ResultsViewModel();
        model.Replace([FileAt("high", 10), FileAt("low", 0)]);
        foreach (var row in model.Rows) row.Marked = true;
        model.Filter("high", 0, SignatureFilter.All);
        Assert.HasCount(2, model.Manual());
        Assert.AreEqual(1, model.LastCopiesSelected());

        model.RemoveMoved([new(model.Rows[1].File, true, null)]);
        Assert.AreEqual(1, model.MarkedCount);
        Assert.AreEqual(0, model.Rows.Single().CurrentGroup);
        Assert.IsEmpty(model.Plan(false).Items);
    });

    [TestMethod]
    public Task MainAndOptionsUseTheSharedPalettesAndLocalizedDialogs() => WpfTestHost.RunAsync((app, strings) =>
    {
        var theme = new ThemeService(app);
        app.Dispatcher.VerifyAccess();
        theme.Apply(AppTheme.Dark);
        strings.SetCulture("de");
        var model = new ResultsViewModel();
        var settings = new Preferences();
        var window = new MainWindow(model, new DuplicateScanner(), new CleanupService(new WindowsRecycleBin()), settings, Store, theme);
        window.Show();
        var button = (Button)window.FindName("RecycleButton");
        Assert.IsFalse(button.IsEnabled);
        model.Replace([FileAt("one", 3)]);
        window.UpdateLayout();
        var grid = (DataGrid)window.FindName("Files");
        var group = VisualChildren<Expander>(grid).First();
        group.IsExpanded = true;
        window.UpdateLayout();

        var checkbox = VisualChildren<CheckBox>(grid).Single();
        var peer = new CheckBoxAutomationPeer(checkbox);
        ((IToggleProvider)peer.GetPattern(PatternInterface.Toggle)!).Toggle();
        Assert.IsTrue(model.Rows.Single().Marked);
        Assert.IsTrue(button.IsEnabled);
        Assert.AreEqual("In den Papierkorb…", button.Content);
        var options = new OptionsWindow(settings) { Owner = window };
        options.Show();
        Assert.AreEqual("Optionen", options.Title);
        var themes = ((ComboBox)options.FindName("Themes")).Items
            .Cast<FilterOption<AppTheme>>().ToDictionary(theme => theme.Value);
        Assert.AreEqual("Hell", themes[AppTheme.Light].Label);
        Assert.AreEqual("Dunkel", themes[AppTheme.Dark].Label);

        strings.SetCulture("zh-Hans");
        Assert.AreEqual("选项", options.Title);
        Assert.AreEqual("浅色", themes[AppTheme.Light].Label);
        Assert.AreEqual("深色", themes[AppTheme.Dark].Label);

        var shortcutsButton = (Button)window.FindName("ShortcutsButton");
        var shortcuts = new ShortcutsWindow { Owner = window };
        shortcuts.Show();
        Assert.AreEqual(strings["ShortcutsTitle"], shortcuts.Title);
        Assert.AreEqual(strings["ShortcutsButton"], AutomationProperties.GetName(shortcutsButton));

        var firstTab = (TabItem)options.FindName("SearchTab");
        Assert.AreEqual(strings["SearchFolders"], firstTab.Header);
        var shortcut = (HeaderedContentControl)shortcuts.FindName("FilterShortcut");
        strings.SetCulture("de");
        shortcuts.UpdateLayout();
        Assert.AreEqual(strings["ShortcutsTitle"], shortcuts.Title);
        Assert.AreEqual(strings["ShortcutsButton"], AutomationProperties.GetName(shortcutsButton));
        Assert.AreEqual("Suchordner", firstTab.Header);
        Assert.IsTrue(VisualChildren<TextBlock>(shortcut).Any(text => text.Text == "Strg+F"));
        Assert.IsTrue(VisualChildren<TextBlock>(shortcut).Any(text => text.Text == strings["KeyFilter"]));

        foreach (var (name, glyph) in new[] { ("OptionsButton", "\uE713"), ("ShortcutsButton", "\uE765"), ("AboutButton", "\uE946") })
        {
            var command = (Button)window.FindName(name);
            Assert.IsTrue(VisualChildren<TextBlock>(command).Any(text => text.Text == glyph && text.FontFamily.Source == "Segoe MDL2 Assets"));
        }
    });

    [TestMethod]
    public Task GroupHeadersOpenAndCloseWithoutMarkingFiles() => WpfTestHost.RunAsync((app, strings) =>
    {
        var theme = new ThemeService(app);
        foreach (var mode in new[] { AppTheme.Light, AppTheme.Dark })
        {
            theme.Apply(mode);
            var model = new ResultsViewModel();
            model.Replace([
                FileAt(@"C:\archive\keep.dat", 10),
                FileAt(@"C:\backup\copy.dat", 1),
                new FoundFile(@"C:\third\single.dat", new("third", 0))
            ]);
            var window = ShowResults(model, theme);
            var grid = (DataGrid)window.FindName("Files");
            window.UpdateLayout();

            Assert.AreEqual(strings["Name"], grid.Columns[0].Header);
            Assert.IsFalse(VisualChildren<Button>(grid).Any(b => b.Command == DataGrid.SelectAllCommand && b.IsVisible));
            var groups = VisualChildren<Expander>(grid).ToArray();
            var duplicates = groups.Single(g => AutomationProperties.GetName(g) == model.Rows[0].GroupTitle);
            var singles = groups.Single(g => AutomationProperties.GetName(g) == strings["Singles"]);
            Assert.IsFalse(duplicates.IsExpanded || singles.IsExpanded);

            ToggleGroupHeader(grid, duplicates);
            Assert.IsTrue(duplicates.IsExpanded);
            Assert.IsFalse(singles.IsExpanded);
            Assert.AreEqual(0, model.MarkedCount);

            var rows = VisualChildren<DataGridRow>(duplicates).Where(r => r.IsVisible).ToArray();
            Assert.HasCount(2, rows);
            var row = rows.Single(r => r.Item == model.Rows[1]);
            var nameCell = VisualChildren<DataGridCell>(row).Single(c => c.Column == grid.Columns[0]);
            Assert.IsTrue(VisualChildren<TextBlock>(nameCell).Any(t => t.Text == "copy.dat"));
            var box = VisualChildren<CheckBox>(nameCell).Single();
            Assert.AreEqual(strings["Mark"], box.ToolTip);
            var peer = new CheckBoxAutomationPeer(box);
            ((IToggleProvider)peer.GetPattern(PatternInterface.Toggle)!).Toggle();
            window.UpdateLayout();

            var cells = VisualChildren<DataGridCellsPanel>(row).Single();
            var mark = Assert.IsInstanceOfType<SolidColorBrush>(cells.Background);
            Assert.AreEqual(SystemColors.AccentColor, mark.Color);
            Assert.IsTrue(model.Rows[1].Marked);

            ToggleGroupHeader(grid, duplicates);
            Assert.IsFalse(duplicates.IsExpanded);
            ToggleGroupHeader(grid, duplicates);
            Assert.IsTrue(duplicates.IsExpanded);
            Assert.IsTrue(VisualChildren<CheckBox>(duplicates).Single(c => c.DataContext == model.Rows[1]).IsChecked);
            Assert.AreEqual(1, model.MarkedCount);

            var name = VisualChildren<TextBlock>(duplicates).Single(text => text.Text == "copy.dat");
            name.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
            {
                RoutedEvent = Mouse.PreviewMouseDownEvent
            });
            Assert.AreEqual(0, model.MarkedCount);
            window.Close();
        }
    });

    private static void ToggleGroupHeader(DataGrid grid, Expander group)
    {
        var toggle = (ToggleButton)group.Template.FindName("HeaderSite", group);
        var title = VisualChildren<TextBlock>(toggle).Single(text => text.Text == AutomationProperties.GetName(group));

        // Hit-test the gap immediately after the title, inside the header and within the visible viewport.
        var point = title.TranslatePoint(new Point(title.ActualWidth + 3, title.ActualHeight / 2), grid);
        var hit = grid.InputHitTest(point) as Visual;
        Assert.IsTrue(hit == toggle || (hit != null && toggle.IsAncestorOf(hit)), "The group header must receive pointer input.");

        var peer = new ToggleButtonAutomationPeer(toggle);
        ((IToggleProvider)peer.GetPattern(PatternInterface.Toggle)!).Toggle();
        grid.UpdateLayout();
    }

    [TestMethod]
    public Task SelectedFileDetailsSurviveFocusChangesAndFollowTheVisibleSelection() => WpfTestHost.RunAsync((app, strings) =>
    {
        strings.SetCulture("de");
        var date = new DateTime(2026, 2, 3, 17, 42, 0, DateTimeKind.Utc);
        var first = FileAt("archive.dat", 10);
        first.Stamp = new(new FileKey(1, Guid.Empty), 123, date, 0, 1);
        var model = new ResultsViewModel();
        model.Replace([first, FileAt("backup.dat", 1)]);
        var window = ShowResults(model, new ThemeService(app));
        var grid = ExpandResults(window);
        var panel = (Expander)window.FindName("DetailsExpander");
        var path = (TextBlock)window.FindName("DetailsPath");
        var details = (TextBox)window.FindName("DetailsBox");

        var row = model.Rows[0];
        Assert.IsFalse(panel.IsExpanded);
        grid.SelectedItem = row;
        grid.CurrentCell = new DataGridCellInfo(row, grid.Columns[1]);
        panel.IsExpanded = true;
        window.UpdateLayout();
        Assert.AreEqual(first.Path, path.Text);
        var modifiedColumn = grid.Columns.Single(c => Equals(c.Header, strings["Modified"]));
        grid.ScrollIntoView(row, modifiedColumn);
        window.UpdateLayout();
        var expectedTime = date.ToString("g", strings.Culture) + " UTC";
        var modifiedCell = VisualChildren<DataGridCell>(grid).Single(cell => cell.Column == modifiedColumn && cell.DataContext == row);
        Assert.AreEqual(expectedTime, ((TextBlock)modifiedCell.Content).Text);
        Assert.Contains(expectedTime, details.Text);

        details.Focus();
        grid.CurrentCell = new DataGridCellInfo();
        details.SelectAll();
        Assert.AreEqual(row.Details, details.SelectedText);
        Assert.IsTrue(ApplicationCommands.Copy.CanExecute(null, details));
        Assert.AreEqual(0, model.MarkedCount);
        panel.IsExpanded = false;
        panel.IsExpanded = true;
        Assert.AreEqual(row.Details, details.Text);

        grid.SelectedItem = model.Rows[1];
        Assert.AreEqual("backup.dat", path.Text);
        Assert.AreEqual("", model.Rows[1].ModifiedText);
        model.Filter("no matches", 0, SignatureFilter.All);
        window.UpdateLayout();
        Assert.AreEqual("", details.Text);
        Assert.AreEqual("", path.Text);
    });

    [TestMethod]
    public Task BusyDarkGridKeepsItsSurfaceAndOnlyLocksMarkingAndFileActions() => WpfTestHost.RunAsync((app, strings) =>
    {
        var theme = new ThemeService(app);
        theme.Apply(AppTheme.Dark);
        var model = new ResultsViewModel();
        model.Replace([FileAt("keep.dat", 10)]);
        model.Rows[0].Marked = true;
        var window = ShowResults(model, theme);
        var grid = ExpandResults(window);
        var check = VisualChildren<CheckBox>(grid).Single();
        var recycle = (Button)window.FindName("RecycleButton");
        var surface = VisualChildren<Border>(grid).First(border => border.TemplatedParent == grid);
        var normalColor = ((SolidColorBrush)surface.Background).Color;
        var line = window.FindResource("LineBrush");
        Assert.AreSame(line, ((Border)window.FindName("ResultsFrame")).BorderBrush);
        Assert.AreSame(line, ((Border)window.FindName("MenuFrame")).BorderBrush);
        Assert.IsTrue(recycle.IsEnabled && check.IsEnabled);

        var cancel = (Button)window.FindName("CancelButton");
        var options = (Button)window.FindName("OptionsButton");
        Assert.IsTrue(options.IsEnabled && options.Command.CanExecute(null));
        var header = (TextBlock)window.FindName("DetailsPath");
        (bool GridEnabled, bool CanMark, bool CanRecycle, bool CanCancel, Color Background, string? Path) busy = default;
        (bool Enabled, bool CanExecute, double Opacity) busyOptions = default;
        var job = window.RunJobAsync(_ =>
        {
            grid.SelectedItem = model.Rows[0];
            window.UpdateLayout();
            busyOptions = (options.IsEnabled, options.Command.CanExecute(null), options.Opacity);
            busy = (grid.IsEnabled, check.IsEnabled, recycle.IsEnabled, cancel.IsEnabled, ((SolidColorBrush)surface.Background).Color, header.Text);
            return Task.CompletedTask;
        });

        Assert.IsFalse(busyOptions.Enabled || busyOptions.CanExecute);
        Assert.IsLessThan(1.0, busyOptions.Opacity);
        Assert.IsTrue(busy.GridEnabled && busy.CanCancel);
        Assert.IsFalse(busy.CanMark || busy.CanRecycle);
        Assert.AreEqual(normalColor, busy.Background);
        Assert.AreEqual("keep.dat", busy.Path);
        Assert.IsTrue(job.IsCompletedSuccessfully && recycle.IsEnabled && check.IsEnabled);
        Assert.IsTrue(options.IsEnabled && options.Command.CanExecute(null));
        Assert.AreEqual(1.0, options.Opacity);
        Assert.AreEqual(1, model.MarkedCount);
    });

    private MainWindow ShowResults(ResultsViewModel model, IThemeService theme, Preferences? preferences = null)
    {
        var window = new MainWindow(model, new DuplicateScanner(), new CleanupService(new WindowsRecycleBin()), preferences ?? new Preferences(), Store, theme);
        window.Show();
        return window;
    }

    private static DataGrid ExpandResults(MainWindow window)
    {
        var grid = (DataGrid)window.FindName("Files");
        window.UpdateLayout();
        foreach (var group in VisualChildren<Expander>(grid).ToArray()) group.IsExpanded = true;
        window.UpdateLayout();
        return grid;
    }

    [TestMethod]
    public Task SizeOptionsValidateAndRestoreIndependentUnits() => WpfTestHost.RunAsync((app, strings) =>
    {
        strings.SetCulture("de");
        var legacy = JsonSerializer.Deserialize<Preferences>("{}")!;
        var dialog = new OptionsWindow(legacy);
        var minimum = (TextBox)dialog.FindName("MinimumSize");
        var maximum = (TextBox)dialog.FindName("MaximumSize");
        var save = (Button)dialog.FindName("SaveButton");
        var message = (TextBlock)dialog.FindName("Validation");
        Assert.AreEqual("", minimum.Text);
        Assert.AreEqual("", maximum.Text);
        Assert.AreEqual(default(FileSizeRange), legacy.Search.SizeRange);

        string? invalidNumber = null;
        string? reversedRange = null;
        Preferences? afterInvalidRange = null;
        dialog.ContentRendered += (_, _) =>
        {
            try
            {
                ((ComboBox)dialog.FindName("MinimumUnit")).SelectedValue = FileSizeUnit.Kilobytes;
                ((ComboBox)dialog.FindName("MaximumUnit")).SelectedValue = FileSizeUnit.Megabytes;
                minimum.Text = "200";
                maximum.Text = "x";
                save.Command.Execute(null);
                invalidNumber = message.Text;

                maximum.Text = "0,1";
                save.Command.Execute(null);
                reversedRange = message.Text;
                afterInvalidRange = dialog.Result;

                maximum.Text = "1,5";
                save.Command.Execute(null);
            }
            finally { if (dialog.IsVisible) dialog.Close(); }
        };

        Assert.IsTrue(dialog.ShowDialog());
        Assert.AreEqual(strings["BadSize"], invalidNumber);
        Assert.AreEqual(strings["ReversedSizes"], reversedRange);
        Assert.AreSame(legacy, afterInvalidRange);

        var saved = JsonSerializer.Deserialize<Preferences>(JsonSerializer.Serialize(dialog.Result))!;
        Assert.AreEqual(new FileSizeRange(200_000, 1_500_000), saved.Search.SizeRange);
        var reopened = new OptionsWindow(saved);
        Assert.AreEqual("200", ((TextBox)reopened.FindName("MinimumSize")).Text);
        Assert.AreEqual("1,5", ((TextBox)reopened.FindName("MaximumSize")).Text);
        Assert.AreEqual(FileSizeUnit.Kilobytes, ((ComboBox)reopened.FindName("MinimumUnit")).SelectedValue);
        Assert.AreEqual(FileSizeUnit.Megabytes, ((ComboBox)reopened.FindName("MaximumUnit")).SelectedValue);
        reopened.Close();
    });

    [TestMethod]
    public Task AppearanceUpdatesExistingResultsAndSaveKeepsTheChosenDisplay() => WpfTestHost.RunAsync((app, strings) =>
    {
        var theme = new ThemeService(app);
        var file = FileAt("keep.dat", 1);
        file.Hashes = new Dictionary<ChecksumAlgorithm, string> { [ChecksumAlgorithm.SHA256] = "FBFF" };
        var model = new ResultsViewModel();
        model.Replace([file]);
        var window = ShowResults(model, theme);
        var row = model.Rows.Single();
        row.Marked = true;
        var path = ((DataGrid)window.FindName("Files")).Columns.Last();
        Assert.IsTrue(path.Width.IsAuto);

        void ChooseAppearance(OptionsWindow dialog)
        {
            ((ComboBox)dialog.FindName("Languages")).SelectedValue = "de";
            ((ComboBox)dialog.FindName("Themes")).SelectedValue = AppTheme.Dark;
            ((ComboBox)dialog.FindName("HashFormats")).SelectedValue = HashFormat.Base64Url;
            ((CheckBox)dialog.FindName("AutoPathWidth")).IsChecked = false;

            Assert.AreEqual(strings["OptionsTitle"], dialog.Title);
            Assert.AreEqual(AppTheme.Dark, theme.CurrentTheme);
            Assert.AreEqual("-_8", row.Sha256);
            StringAssert.Contains(row.Details, "-_8");
            Assert.AreEqual(320, path.Width.Value, 0.01);
        }

        InOptions(window, dialog =>
        {
            ((ComboBox)dialog.FindName("MinimumUnit")).SelectedValue = FileSizeUnit.Kilobytes;
            var minimum = (TextBox)dialog.FindName("MinimumSize");
            minimum.Text = "1.5";
            ChooseAppearance(dialog);
            Assert.AreEqual("1,5", minimum.Text);
        });
        Assert.AreEqual("en", strings.Culture.Name);
        Assert.AreEqual(AppTheme.Light, theme.CurrentTheme);
        Assert.AreEqual("FBFF", row.Sha256);
        Assert.IsTrue(path.Width.IsAuto);

        InOptions(window, dialog =>
        {
            ChooseAppearance(dialog);
            ((Button)dialog.FindName("SaveButton")).Command.Execute(null);
        });
        var saved = Store.Load();
        Assert.AreEqual(("de", AppTheme.Dark, HashFormat.Base64Url, false),
            (saved.Language, saved.Theme, saved.HashFormat, saved.PathAutoWidth));
        Assert.IsNull(saved.SizeRange.MinimumBytes);
        Assert.AreSame(row, model.Rows.Single());
        Assert.IsTrue(row.Marked);
        Assert.AreEqual("FBFF", file.Hashes[ChecksumAlgorithm.SHA256]);
        model.Filter("-_8", 0, SignatureFilter.All);
        Assert.HasCount(1, model.View.Cast<ResultRow>().ToArray());
    });

    [TestMethod]
    public Task MainAndOptionsRestoreIndependentNormalSizesAndMaximizedState() => WpfTestHost.RunAsync((app, _) =>
    {
        var theme = new ThemeService(app);
        var window = ShowResults(new ResultsViewModel(), theme);
        var area = SystemParameters.WorkArea;
        var mainWidth = Math.Min(area.Width, 840);
        var optionsWidth = Math.Min(area.Width, 680);
        var mainHeight = Math.Min(area.Height, 630);
        var optionsHeight = Math.Min(area.Height, 650);
        window.Width = mainWidth;
        window.Height = mainHeight;

        InOptions(window, dialog =>
        {
            dialog.Width = optionsWidth;
            dialog.Height = optionsHeight;
            DrainLayout();
            dialog.WindowState = WindowState.Maximized;
            DrainLayout();
        });
        window.WindowState = WindowState.Maximized;
        DrainLayout();
        window.WindowState = WindowState.Minimized;
        DrainLayout();
        window.Close();

        var saved = Store.Load();
        Assert.IsNotNull(saved.MainWindow);
        Assert.IsNotNull(saved.OptionsWindow);
        Assert.IsTrue(saved.MainWindow.Maximized && saved.OptionsWindow.Maximized);
        var reopened = ShowResults(new ResultsViewModel(), theme, saved);
        Assert.AreEqual(WindowState.Maximized, reopened.WindowState);
        reopened.WindowState = WindowState.Normal;
        DrainLayout();
        Assert.AreEqual(mainWidth, reopened.Width, 2);
        Assert.AreEqual(mainHeight, reopened.Height, 2);

        InOptions(reopened, dialog =>
        {
            Assert.AreEqual(WindowState.Maximized, dialog.WindowState);
            dialog.WindowState = WindowState.Normal;
            DrainLayout();
            Assert.AreEqual(optionsWidth, dialog.Width, 2);
            Assert.AreEqual(optionsHeight, dialog.Height, 2);
        });
    });

    [TestMethod]
    public Task DesignerResolvesProductTextsAndSharedStylesWithoutApplicationStartup() => WpfTestHost.RunAsync((_, strings) =>
    {
        strings.SetCulture("de");
        var label = new TextBlock();
        DesignerProperties.SetIsInDesignMode(label, true);
        var target = new DesignTarget(label);
        var resources = new ResourceDictionary
        {
            Source = new Uri("/DuplicateFinder.App;component/Properties/DesignTimeResources.xaml", UriKind.Relative)
        };
        Assert.IsInstanceOfType<Style>(resources["AppWindowStyle"]);
        Assert.IsInstanceOfType<Style>(resources["DefaultDataGridRowStyle"]);
        Assert.IsInstanceOfType<SolidColorBrush>(resources["LineBrush"]);

        strings.UseApplicationResources(typeof(LocalizationService).Assembly);
        try
        {
            Assert.AreEqual("Ordner durchsuchen", new DuplicateFinder.App.LocExtension("Scan").ProvideValue(target));
            Assert.AreEqual(strings["Signature_NotChecked"], new DuplicateFinder.App.LocExtension("Signature_NotChecked").ProvideValue(target));
        }
        finally { strings.UseApplicationResources(typeof(MainWindow).Assembly); }
    });

    private sealed class DesignTarget(TextBlock element) : IServiceProvider, IProvideValueTarget
    {
        public object TargetObject => element;
        public object TargetProperty => TextBlock.TextProperty;
        public object? GetService(Type serviceType) => serviceType == typeof(IProvideValueTarget) ? this : null;
    }

    private static void DrainLayout()
    {
        var frame = new DispatcherFrame();
        _ = Dispatcher.CurrentDispatcher.InvokeAsync(() => frame.Continue = false, DispatcherPriority.ApplicationIdle);
        Dispatcher.PushFrame(frame);
    }

    private static void InOptions(MainWindow owner, Action<OptionsWindow> inspect)
    {
        Assert.IsTrue(owner.OptionsCommand.CanExecute(null));
        Exception? error = null;
        var handled = false;
        var operation = owner.Dispatcher.InvokeAsync(() =>
        {
            var dialog = Application.Current.Windows.OfType<OptionsWindow>().Single(w => w.Owner == owner);
            handled = true;
            try { inspect(dialog); }
            catch (Exception ex) { error = ex; }
            finally { if (dialog.IsVisible) dialog.Close(); }
        }, DispatcherPriority.ApplicationIdle);

        owner.OptionsCommand.Execute(null);
        if (!handled) operation.Abort();
        Assert.IsTrue(handled, "The options command must open an owned modal window.");
        if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
    }

    private static IEnumerable<T> VisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var descendant in VisualChildren<T>(child)) yield return descendant;
        }
    }
}
