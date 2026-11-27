using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using FileScope.Core.Models;
using FileScope.Core.Signatures;

namespace FileScope.Ui.Tests;

internal static class CatalogBadgeChecks
{
    internal static CatalogLookup MixedLookup()
    {
        var valid = new CatalogMatch("first.cat", "member", false, new(SignatureStatus.Valid, RevocationMode.Online));
        var revoked = valid with { Path = "revoked.cat", Trust = new(SignatureStatus.Revoked, RevocationMode.OfflineCache) };

        return new([revoked, valid, valid with { Path = "another.cat" }], ["unreadable catalog"], false);
    }

    internal static void Select(ComboBox box, byte code) =>
        box.SelectedItem = box.Items.Cast<FilterOption<SignatureFilter>>().Single(option => option.Value.Group?.Code == code);

    internal static void CheckMixedRow(DataGrid grid, DataGridColumn column, object row, LocalizationService strings)
    {
        grid.ScrollIntoView(row, column);
        grid.UpdateLayout();
        grid.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        grid.UpdateLayout();

        var content = column.GetCellContent(row);
        Assert.IsNotNull(content);
        var badges = VisualTreeQueries.VisualChildren<StatusBadge>(content).ToArray();
        Assert.HasCount(3, badges);
        CheckBadge(badges[0], "11 | " + strings["Signature_ValidOnline"], StatusTone.Success, "OnSuccessBrush");
        CheckBadge(badges[1], "17 | " + strings["Signature_Revoked"], StatusTone.Error, "OnErrorBrush");
        CheckBadge(badges[2], "1D | " + strings["Incomplete"], StatusTone.Warning, "OnWarningBrush");

        var cell = DataGridRow.GetRowContainingElement(content);
        var last = badges[^1];
        Assert.IsTrue(last.TranslatePoint(new Point(0, last.ActualHeight), cell).Y <= cell.ActualHeight + 1,
            "The last catalog badge fits inside the visible row.");
        Assert.AreEqual(string.Join(" · ", badges.Select(badge => badge.Text)), column.OnCopyingCellClipboardContent(row));

        void CheckBadge(StatusBadge badge, string label, StatusTone tone, string brushKey)
        {
            Assert.IsTrue(badge.IsVisible && badge.ActualHeight > 0);
            Assert.AreEqual(label, badge.Text);
            Assert.AreEqual(label, AutomationProperties.GetName(badge));
            Assert.AreEqual(tone, badge.Tone);
            Assert.AreEqual(((SolidColorBrush)grid.FindResource(brushKey)).Color, ((SolidColorBrush)((Border)badge.Content).Background).Color);
        }
    }
}
