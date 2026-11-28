using System.Windows;
using System.Windows.Controls;

namespace FileScope.Ui.Tests;

internal static class MenuButtonChecks
{
    public static void Check(Window window, LocalizationService strings)
    {
        (string Name, string Glyph, string Key)[] buttons =
        [
            ("OptionsButton", "\uE713", "Options"),
            ("AboutButton", "\uE946", "AboutButton"),
            ("ShortcutsButton", "\uE765", "ShortcutsButton")
        ];

        foreach (var (name, glyph, key) in buttons)
        {
            var button = (Button)window.FindName(name);
            var texts = VisualTreeQueries.VisualChildren<TextBlock>(button).ToArray();
            Assert.IsTrue(texts.Any(text => text.Text == glyph && text.FontFamily.Source == "Segoe MDL2 Assets"), name);
            Assert.IsTrue(texts.Any(text => text.Text == strings[key]), $"{name} / {strings.Culture.Name}");
        }
    }
}
