using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using DuplicateFinder.App.ViewModels;
using DuplicateFinder.Core.Models;
using FileScope.Ui.Tests;

namespace DuplicateFinder.Ui.Tests;

public sealed partial class WindowTests
{
    [TestMethod]
    public Task TableShortcutsSelectAndCopyFilteredRowsIndependentlyOfRecycleMarks() => WpfTestHost.RunAsync((app, strings) =>
    {
        var model = new ResultsViewModel();
        var single = new FoundFile(@"C:\single\match-single.bin", new("search", 0));
        model.Replace([FileAt(@"C:\archive\match-a.bin", 3), FileAt(@"C:\backup\match-b.bin", 2),
            FileAt(@"C:\download\match-c.bin", 1), single, FileAt(@"C:\other.bin", 0)]);
        var window = ShowResults(model, new ThemeService(app));
        ((TextBox)window.FindName("FilterBox")).Text = "match";
        var grid = ExpandResults(window);
        var files = model.View.Cast<ResultRow>().ToArray();
        Assert.HasCount(4, files);

        var first = VisualChildren<DataGridCell>(grid).Single(cell => cell.Column == grid.Columns[1] && cell.DataContext == files[0]);
        Assert.IsTrue(first.Focus());
        PressKey(Key.Down);
        Assert.AreSame(files[1], grid.SelectedItem);
        PressKey(Key.Up, ModifierKeys.Shift);
        CollectionAssert.AreEquivalent(files[..2], grid.SelectedItems.Cast<ResultRow>().ToArray());
        PressKey(Key.Down, ModifierKeys.Shift);
        Assert.HasCount(1, grid.SelectedItems);

        var panel = (Expander)window.FindName("DetailsExpander");
        var details = (TextBox)window.FindName("DetailsBox");
        panel.IsExpanded = true;
        window.UpdateLayout();
        Assert.IsTrue(details.Focus());
        PressKey(Key.A, ModifierKeys.Control);
        Assert.AreEqual(files[1].Details, details.SelectedText);
        Assert.IsTrue(ApplicationCommands.Copy.CanExecute(null, details));
        Assert.HasCount(1, grid.SelectedItems);

        var singlesGroup = VisualChildren<Expander>(grid).Single(group => AutomationProperties.GetName(group) == strings["Singles"]);
        singlesGroup.IsExpanded = false;
        window.UpdateLayout();
        Assert.IsTrue(first.Focus());
        PressKey(Key.A, ModifierKeys.Control);
        CollectionAssert.AreEquivalent(files, grid.SelectedItems.Cast<ResultRow>().ToArray());
        Assert.IsFalse(singlesGroup.IsExpanded);
        Assert.AreEqual("", details.Text);
        Assert.AreEqual(0, model.MarkedCount);
        Assert.IsFalse(window.RecycleCommand.CanExecute(null));

        // Capture the actual clipboard rows. Stop before WPF replaces the user's system clipboard.
        var names = new List<object?>();
        var paths = new List<object?>();
        var captured = new OperationCanceledException("Clipboard rows captured.");
        grid.CopyingRowClipboardContent += (_, row) =>
        {
            names.Add(row.ClipboardRowContent[0].Content);
            paths.Add(row.ClipboardRowContent[^1].Content);
            if (names.Count == files.Length + 1) throw captured;
        };

        Assert.AreSame(captured, Assert.ThrowsExactly<OperationCanceledException>(() => PressKey(Key.C, ModifierKeys.Control)));
        CollectionAssert.AreEqual(new[] { strings["Name"] }.Concat(files.Select(row => row.File.Name)).ToArray(), names.ToArray());
        CollectionAssert.AreEqual(new[] { strings["Path"] }.Concat(files.Select(row => row.File.Path)).ToArray(), paths.ToArray());
    });

    private static void PressKey(Key key, ModifierKeys modifier = ModifierKeys.None)
    {
        var target = Assert.IsInstanceOfType<UIElement>(Keyboard.FocusedElement);
        var original = new byte[256];
        Assert.AreNotEqual(0, GetKeyboardState(original));

        // WPF reads modifiers from this STA thread's input queue, even for routed key events.
        var state = new byte[256];
        state[KeyInterop.VirtualKeyFromKey(key)] = 0x80;
        if (modifier == ModifierKeys.Shift) state[0x10] = state[KeyInterop.VirtualKeyFromKey(Key.LeftShift)] = 0x80;
        if (modifier == ModifierKeys.Control) state[0x11] = state[KeyInterop.VirtualKeyFromKey(Key.LeftCtrl)] = 0x80;

        try
        {
            Assert.AreNotEqual(0, SetKeyboardState(state));
            var args = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(target)!, Environment.TickCount, key)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent
            };
            target.RaiseEvent(args);
            if (!args.Handled)
            {
                args.RoutedEvent = Keyboard.KeyDownEvent;
                target.RaiseEvent(args);
            }
        }
        finally
        {
            Assert.AreNotEqual(0, SetKeyboardState(original));
        }
    }

#pragma warning disable SYSLIB1054 // Keep the test project free of unsafe code.
    [DllImport("user32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int GetKeyboardState([Out] byte[] state);

    [DllImport("user32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int SetKeyboardState([In] byte[] state);
#pragma warning restore SYSLIB1054
}
