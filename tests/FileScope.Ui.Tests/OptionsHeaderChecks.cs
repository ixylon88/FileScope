using System.Windows;
using System.Windows.Controls;

namespace FileScope.Ui.Tests;

internal static class OptionsHeaderChecks
{
    public static OptionsHeader From(Window window) => (OptionsHeader)window.FindName("DisplayOptions");

    public static void CheckLayout(Window window, TabItem item)
    {
        var header = From(window);
        var tabs = (TabControl)ItemsControl.ItemsControlFromItemContainer(item);
        var original = tabs.SelectedIndex;
        var strings = LocalizationService.Instance;

        var themes = header.ThemeSelector.Items.Cast<FilterOption<AppTheme>>().ToArray();
        CollectionAssert.AreEqual(new[] { AppTheme.Light, AppTheme.Dark }, themes.Select(option => option.Value).ToArray());
        foreach (var option in themes) Assert.AreEqual(strings["Theme_" + option.Value], option.Label);

        var formats = header.HashFormatSelector.Items.Cast<FilterOption<HashFormat>>().ToArray();
        CollectionAssert.AreEqual(Enum.GetValues<HashFormat>(), formats.Select(option => option.Value).ToArray());
        foreach (var option in formats) Assert.AreEqual(strings["HashFormat_" + option.Value], option.Label);

        try
        {
            foreach (var tab in tabs.Items.Cast<TabItem>())
            {
                tabs.SelectedItem = tab;
                window.UpdateLayout();
                var theme = header.ThemeSelector;
                var format = header.HashFormatSelector;
                var language = header.LanguageSelector;
                foreach (var selector in new[] { theme, format, language })
                {
                    Assert.IsTrue(selector.IsVisible);
                    Assert.AreEqual(34.0, selector.MinHeight, "The minimum applies to the clickable control.");
                }

                var top = theme.TranslatePoint(new Point(theme.ActualWidth, theme.ActualHeight), window);
                var left = format.TranslatePoint(new Point(format.ActualWidth, 0), window);
                var right = language.TranslatePoint(new Point(), window);

                Assert.IsTrue(top.Y <= left.Y, "Theme sits above the two other selectors.");
                Assert.AreEqual(right.X + language.ActualWidth, top.X, 1, "Theme is aligned to the right edge.");
                Assert.AreEqual(left.Y, right.Y, 1, "Hash format and language share a row.");
                Assert.AreEqual(16, right.X - left.X, 1, "Language follows hash format with a 16-DIP gap.");
                Assert.IsTrue(left.Y + format.ActualHeight <= tabs.TranslatePoint(new Point(), window).Y);
            }
        }
        finally { tabs.SelectedIndex = original; window.UpdateLayout(); }
    }
}
