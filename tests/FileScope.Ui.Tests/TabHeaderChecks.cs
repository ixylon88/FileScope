using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace FileScope.Ui.Tests;

internal static class TabHeaderChecks
{
    public static void Check(TabItem item)
    {
        var tabs = (TabControl)ItemsControl.ItemsControlFromItemContainer(item);
        var original = tabs.SelectedIndex;
        try
        {
            foreach (var selected in tabs.Items.Cast<TabItem>())
            {
                tabs.SelectedItem = selected;
                tabs.UpdateLayout();

                foreach (var tab in tabs.Items.Cast<TabItem>())
                {
                    var border = (Border)tab.Template.FindName("Border", tab);
                    var hover = (SolidColorBrush)tab.FindResource("DataGridHeaderBackgroundPointerOver");
                    if (tab.IsSelected)
                    {
                        var accent = (SolidColorBrush)tab.FindResource("SystemFillColorAttentionBrush");
                        var text = (SolidColorBrush)tab.FindResource("TextOnAccentFillColorSelectedTextBrush");
                        var header = (ContentPresenter)tab.Template.FindName("ContentSite", tab);

                        Assert.AreEqual(accent.Color, ((SolidColorBrush)border.Background).Color);
                        Assert.AreEqual(text.Color, ((SolidColorBrush)TextElement.GetForeground(header)).Color);
                    }
                    else
                    {
                        var expected = tab.IsMouseOver ? hover : (SolidColorBrush)tab.FindResource("TabViewItemHeaderBackground");
                        Assert.AreEqual(expected.Color, ((SolidColorBrush)border.Background).Color);
                    }
                }
            }
        }
        finally { tabs.SelectedIndex = original; tabs.UpdateLayout(); }
    }
}
