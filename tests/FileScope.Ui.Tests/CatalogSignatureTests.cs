using System.IO;
using System.Windows.Controls;
using FileScope.App;
using FileScope.App.Behaviors;
using FileScope.App.ViewModels;

namespace FileScope.Ui.Tests;

public sealed partial class UiTests
{
    [TestMethod]
    public Task CatalogFiltersAndBadgesFollowTheCurrentRowLanguageAndTheme() => WpfTestHost.RunAsync((app, strings) =>
    {
        using var options = new OptionsViewModel(strings, new MemoryPreferences(), new ThemeService(app));
        using var vm = CreateMainViewModel(new ImmediateAnalysis(), new EmptyPicker(), strings, options);
        vm.InspectCatalogs = true;
        var window = new MainWindow(vm) { ShowActivated = false, ShowInTaskbar = false };
        window.Show();

        vm.DropFilesCommand.Execute(new[] { Path.Combine(Path.GetTempPath(), "member.bin"), Path.Combine(Path.GetTempPath(), "unsearched.bin") });
        PumpUntil(() => !vm.IsBusy && vm.Rows.Count == 2);
        var row = vm.Rows.Single(item => item.FileName == "member.bin");
        row.Complete(row.Result! with { Catalogs = CatalogBadgeChecks.MixedLookup() });

        var grid = (DataGrid)window.FindName("ResultsGrid");
        var column = grid.Columns.Single(c => ColumnLayoutBehavior.GetId(c) == "Catalog");
        var box = (ComboBox)window.FindName("SignatureBox");
        foreach (var code in new byte[] { 0x11, 0x17, 0x1D })
        {
            CatalogBadgeChecks.Select(box, code);
            Assert.AreSame(row, vm.RowsView.Cast<FileRowViewModel>().Single());
        }
        CatalogBadgeChecks.CheckMixedRow(grid, column, row, strings);

        options.SelectedLanguage = options.Languages.Single(language => language.CultureName == "de");
        options.SelectedTheme = options.Themes.Single(theme => theme.Value == AppTheme.Dark);
        DrainDispatcher();
        Assert.AreEqual((byte)0x1D, vm.SignatureFilterOption.Value.Group!.Value.Code);
        CatalogBadgeChecks.CheckMixedRow(grid, column, row, strings);

        CatalogBadgeChecks.Select(box, 0x10);
        Assert.AreEqual("unsearched.bin", vm.RowsView.Cast<FileRowViewModel>().Single().FileName);
        vm.ResetFiltersCommand.Execute(null);
        Assert.AreEqual(2, vm.RowsView.Count);
        window.Close();
    });
}
