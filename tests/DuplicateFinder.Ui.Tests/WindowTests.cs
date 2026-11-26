using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using DuplicateFinder.App;
using DuplicateFinder.Core;
using FileScope.App.Services;
using FileScope.Ui.Tests;

namespace DuplicateFinder.Ui.Tests;

[TestClass]
public sealed class WindowTests
{
    private static FoundFile FileAt(string name, int priority) => new(name, new("search", priority))
    { Group = 1, Variant = 1, Streams = StreamComparison.Identical };

    [TestMethod]
    public Task FilterScopeIncludesCollapsedRowsAndAnInvisibleKeeper() => WpfTestHost.RunAsync((app, strings) =>
    {
        var model = new ResultsViewModel();
        model.Replace([FileAt("archive", 30), FileAt("backup", 20), FileAt("download", 10)]);
        model.Filter("download", 1, null, false);
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
        model.Filter("no match", 0, null, false);
        var dialog = new ScopeWindow(model.Plan(true), model.Plan(false));
        dialog.Show();
        var confirm = (Button)dialog.FindName("Confirm");
        Assert.IsFalse(confirm.IsEnabled);
        ((RadioButton)dialog.FindName("AllFiles")).IsChecked = true;
        Assert.IsTrue(confirm.IsEnabled);
        Assert.IsFalse(dialog.Filtered);
    });

    [TestMethod]
    public Task HiddenMarksSurviveFilterAndWarnAboutLastCopy() => WpfTestHost.RunAsync((app, strings) =>
    {
        var model = new ResultsViewModel();
        model.Replace([FileAt("high", 10), FileAt("low", 0)]);
        foreach (var row in model.Rows) row.Marked = true;
        model.Filter("high", 0, null, false);
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
        var window = new MainWindow(model, new DuplicateScanner(), new CleanupService(new WindowsRecycleBin()), settings, new SettingsStore(), theme);
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
        Assert.AreEqual("In den Papierkorb", button.Content);
        var options = new OptionsWindow(settings) { Owner = window };
        options.Show();
        Assert.AreEqual("Optionen", options.Title);
        strings.SetCulture("zh-Hans");
        Assert.AreEqual("选项", options.Title);
        var shortcuts = new ShortcutsWindow { Owner = window };
        shortcuts.Show();
        Assert.AreEqual("快捷键", shortcuts.Title);

        var firstTab = (TabItem)options.FindName("SearchTab");
        Assert.AreEqual(strings["SearchFolders"], firstTab.Header);
        var shortcut = (HeaderedContentControl)shortcuts.FindName("FilterShortcut");
        strings.SetCulture("de");
        shortcuts.UpdateLayout();
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
        model.Filter("no matches", 0, null, false);
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
        var header = (TextBlock)window.FindName("DetailsPath");
        (bool GridEnabled, bool CanMark, bool CanRecycle, bool CanCancel, Color Background, string? Path) busy = default;
        var job = window.RunJobAsync(_ =>
        {
            grid.SelectedItem = model.Rows[0];
            window.UpdateLayout();
            busy = (grid.IsEnabled, check.IsEnabled, recycle.IsEnabled, cancel.IsEnabled, ((SolidColorBrush)surface.Background).Color, header.Text);
            return Task.CompletedTask;
        });

        Assert.IsTrue(busy.GridEnabled && busy.CanCancel);
        Assert.IsFalse(busy.CanMark || busy.CanRecycle);
        Assert.AreEqual(normalColor, busy.Background);
        Assert.AreEqual("keep.dat", busy.Path);
        Assert.IsTrue(job.IsCompletedSuccessfully && recycle.IsEnabled && check.IsEnabled);
        Assert.AreEqual(1, model.MarkedCount);
    });

    private static MainWindow ShowResults(ResultsViewModel model, IThemeService theme)
    {
        var window = new MainWindow(model, new DuplicateScanner(), new CleanupService(new WindowsRecycleBin()), new Preferences(), new SettingsStore(), theme);
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

        ((ComboBox)dialog.FindName("MinimumUnit")).SelectedValue = FileSizeUnit.Kilobytes;
        ((ComboBox)dialog.FindName("MaximumUnit")).SelectedValue = FileSizeUnit.Megabytes;
        minimum.Text = "200";
        maximum.Text = "x";
        save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.AreEqual(strings["BadSize"], message.Text);

        maximum.Text = "0,1";
        save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.AreEqual(strings["ReversedSizes"], message.Text);
        Assert.AreSame(legacy, dialog.Result);

        maximum.Text = "1,5";
        dialog.ContentRendered += (_, _) =>
        {
            try { save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
            finally { if (dialog.IsVisible) dialog.Close(); }
        };
        Assert.IsTrue(dialog.ShowDialog());

        var saved = JsonSerializer.Deserialize<Preferences>(JsonSerializer.Serialize(dialog.Result))!;
        Assert.AreEqual(new FileSizeRange(200_000, 1_500_000), saved.Search.SizeRange);
        var reopened = new OptionsWindow(saved);
        Assert.AreEqual("200", ((TextBox)reopened.FindName("MinimumSize")).Text);
        Assert.AreEqual("1,5", ((TextBox)reopened.FindName("MaximumSize")).Text);
        Assert.AreEqual(FileSizeUnit.Kilobytes, ((ComboBox)reopened.FindName("MinimumUnit")).SelectedValue);
        Assert.AreEqual(FileSizeUnit.Megabytes, ((ComboBox)reopened.FindName("MaximumUnit")).SelectedValue);
        reopened.Close();
    });

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
