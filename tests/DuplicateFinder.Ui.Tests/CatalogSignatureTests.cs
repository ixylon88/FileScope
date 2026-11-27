using System.Windows.Controls;
using DuplicateFinder.App.ViewModels;
using FileScope.Ui.Tests;

namespace DuplicateFinder.Ui.Tests;

public sealed partial class WindowTests
{
    [TestMethod]
    public Task CatalogFiltersAndBadgesFollowTheLanguageAndKeepRecycleMarks() => WpfTestHost.RunAsync((app, strings) =>
    {
        var member = FileAt("member.bin", 1);
        member.Catalogs = CatalogBadgeChecks.MixedLookup();
        var model = new ResultsViewModel();
        model.Replace([member, FileAt("unsearched.bin", 0)]);
        var row = model.Rows[0];
        model.Rows[1].Marked = true;

        var window = ShowResults(model, new ThemeService(app));
        var box = (ComboBox)window.FindName("SignatureBox");
        foreach (var code in new byte[] { 0x11, 0x17, 0x1D })
        {
            CatalogBadgeChecks.Select(box, code);
            Assert.AreSame(row, model.View.Cast<ResultRow>().Single());
        }

        var kind = (ComboBox)window.FindName("KindBox");
        kind.SelectedValue = ResultKind.Duplicates;
        var selectedKind = (FilterOption<ResultKind>)kind.SelectedItem;
        var selectedSignature = (FilterOption<SignatureFilter>)box.SelectedItem;

        var grid = ExpandResults(window);
        var column = grid.Columns.Single(c => Equals(c.Header, strings["Column_Catalog"]));
        CatalogBadgeChecks.CheckMixedRow(grid, column, row, strings);

        InOptions(window, dialog =>
        {
            var header = OptionsHeaderChecks.From(dialog);
            header.LanguageSelector.SelectedValue = "de";
            header.ThemeSelector.SelectedValue = AppTheme.Dark;
            new OptionsFooterChecks(dialog).Save.Command.Execute(null);
        });
        Assert.AreSame(selectedKind, kind.SelectedItem);
        Assert.AreSame(selectedSignature, box.SelectedItem);
        Assert.AreEqual(strings["Duplicates"], selectedKind.Label);
        Assert.AreEqual((byte)0x1D, ((SignatureFilter)box.SelectedValue).Group!.Value.Code);
        CatalogBadgeChecks.CheckMixedRow(ExpandResults(window), column, row, strings);

        CatalogBadgeChecks.Select(box, 0x10);
        Assert.AreSame(model.Rows[1], model.View.Cast<ResultRow>().Single());
        model.ResetFiltersCommand.Execute(null);
        Assert.AreEqual(2, model.View.Count);
        Assert.IsTrue(model.Rows[1].Marked);
    });
}
