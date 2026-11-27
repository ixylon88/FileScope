using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace FileScope.Ui.Tests;

internal static class DialogPlacementChecks
{
    public static void Check(Window owner, Func<Window> reopenOwner,
        Func<WindowPreferences?> savedAbout, Func<WindowPreferences?> savedShortcuts)
    {
        Show<AboutDialog>(owner, _ => { });
        Show<ShortcutsDialog>(owner, _ => { });
        Assert.IsNull(savedAbout(), "Viewing About must leave the factory default available.");
        Assert.IsNull(savedShortcuts(), "Viewing Shortcuts must leave the factory default available.");

        double width = 0, height = 0;
        Show<AboutDialog>(owner, dialog =>
        {
            width = dialog.Width = dialog.MinWidth;
            height = dialog.Height = dialog.MinHeight;
            dialog.UpdateLayout();
        });

        var about = savedAbout();
        Assert.IsNotNull(about);
        Assert.AreEqual(width, about.Width, 1);
        Assert.AreEqual(height, about.Height, 1);
        Assert.IsNull(savedShortcuts(), "Resizing About must only write About's placement.");

        Show<ShortcutsDialog>(owner, dialog => dialog.WindowState = WindowState.Maximized);
        var shortcuts = savedShortcuts();
        Assert.IsNotNull(shortcuts);
        Assert.IsTrue(shortcuts.Maximized);

        var reopened = reopenOwner();
        try
        {
            Show<AboutDialog>(reopened, dialog =>
            {
                Assert.AreEqual(WindowState.Normal, dialog.WindowState);
                Assert.AreEqual(about.Width, dialog.Width, 1);
                Assert.AreEqual(about.Height, dialog.Height, 1);
            });

            Show<ShortcutsDialog>(reopened, dialog => Assert.AreEqual(WindowState.Maximized, dialog.WindowState));
            Assert.AreEqual(about, savedAbout());
            Assert.AreEqual(shortcuts, savedShortcuts());
        }
        finally { reopened.Close(); }
    }

    private static void Show<T>(Window owner, Action<T> inspect) where T : Window
    {
        var operation = owner.Dispatcher.InvokeAsync(() =>
        {
            var dialog = owner.OwnedWindows.OfType<T>().Single(window => window.IsVisible);
            try { inspect(dialog); }
            finally { dialog.Close(); }
        }, DispatcherPriority.ApplicationIdle);

        if (typeof(T) == typeof(AboutDialog))
            ((Button)owner.FindName("AboutButton")).Command.Execute(null);
        else
            ApplicationCommands.Help.Execute(null, owner);

        // If a binding stops opening its dialog, fail here instead of leaking a queued callback.
        Assert.IsFalse(operation.Abort(), "The application command must open a modal dialog.");
        operation.Task.GetAwaiter().GetResult();
    }
}
