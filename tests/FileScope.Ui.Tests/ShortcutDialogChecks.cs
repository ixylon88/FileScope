using System.Windows.Controls;

namespace FileScope.Ui.Tests;

internal static class ShortcutDialogChecks
{
    public static void Check(ShortcutsDialog dialog, LocalizationService strings, string[] actions, params string[] extraSelection)
    {
        CheckText("ShortcutsTitle", dialog.Title);
        CheckText("ShortcutsHint", ((TextBlock)dialog.FindName("ShortcutsHintText")).Text);
        CheckText("ShortcutsCloseHint", ((TextBlock)dialog.FindName("ShortcutsCloseHintText")).Text);
        var close = (Button)dialog.FindName("CloseShortcutsButton");
        CheckText("Close", close.Content);
        Assert.IsTrue(close.IsCancel && close.IsDefault);
        Assert.AreEqual(strings.Culture.IetfLanguageTag, dialog.Language.IetfLanguageTag, ignoreCase: true);

        CheckGroup("Actions", [.. actions, "Cancel", "Help"]);
        CheckGroup("Navigation", ["TableEnds", "Page", "RowEnds", "Rows"]);
        CheckGroup("Selection", ["SelectAll", "Extend", "Copy", .. extraSelection]);

        void CheckGroup(string group, string[] shortcuts)
        {
            var heading = (TextBlock)dialog.FindName("ShortcutsGroup" + group);
            CheckText("ShortcutsGroup" + group, heading.Text);

            var rows = ((StackPanel)dialog.FindName(group + "List")).Children.Cast<HeaderedContentControl>().ToArray();
            Assert.HasCount(shortcuts.Length, rows, group);
            for (int i = 0; i < shortcuts.Length; i++)
            {
                CheckText($"Shortcut{shortcuts[i]}Keys", rows[i].Header);
                CheckText($"Shortcut{shortcuts[i]}Action", rows[i].Content);
            }
        }

        void CheckText(string key, object? actual)
        {
            var translation = strings[key];
            Assert.AreNotEqual($"[{key}]", translation, $"Missing shortcut text for {key}.");
            Assert.AreEqual(translation, actual, $"{strings.Culture.Name}/{key}");
        }
    }
}
