using System.Text.Json;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
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
    });

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
