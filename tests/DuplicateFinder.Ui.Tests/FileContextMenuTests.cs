using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using DuplicateFinder.App.ViewModels;
using FileScope.Ui.Tests;
using static FileScope.Ui.Tests.FileMenuChecks;

using static FileScope.Ui.Tests.VisualTreeQueries;

namespace DuplicateFinder.Ui.Tests;

public sealed partial class WindowTests
{
    [TestMethod]
    public Task FileMenuUsesSingleSelectionAndConfirmsTheSameRecycleMarksAsTheToolbar() =>
        WpfTestHost.RunAsync((app, strings) =>
    {
        var model = new ResultsViewModel();
        var checkedFile = FileAt(@"C:\archive\keep.dat", 10);
        var clickedFile = FileAt(@"C:\some folder\nested\copy, 中文 & #.dat", 1);
        model.Replace([checkedFile, clickedFile]);
        var shell = new RecordingFileShell();
        var window = ShowResults(model, new ThemeService(app), shell: shell);
        var grid = ExpandResults(window);
        var menu = DataGridRow.GetRowContainingElement(CellFor(model.Rows[1])).ContextMenu;
        var recycleButton = (Button)window.FindName("RecycleButton");
        var errorText = (TextBlock)window.FindName("FileActionErrorText");
        Assert.IsNull(grid.ContextMenu);
        Assert.IsTrue(VisualChildren<GroupItem>(grid).All(group => group.ContextMenu == null));
        Assert.IsFalse(window.ShowFilePropertiesCommand.CanExecute(null) || window.ShowInExplorerCommand.CanExecute(null));

        try
        {
            grid.SelectedItem = model.Rows[0];
            OpenOn(model.Rows[1]);
            Assert.AreSame(model.Rows[1], grid.SelectedItem);
            Assert.AreEqual(0, model.MarkedCount);
            var recycle = MenuFor(menu, window.RecycleCommand);
            var properties = MenuFor(menu, window.ShowFilePropertiesCommand);
            var explorer = MenuFor(menu, window.ShowInExplorerCommand);
            Assert.AreSame(recycleButton.Command, recycle.Command);
            Assert.IsFalse(recycle.IsEnabled);
            Assert.IsTrue(properties.IsEnabled && explorer.IsEnabled);
            InvokeMenuItem(properties);
            DrainLayout();

            OpenOn(model.Rows[1]);
            shell.Succeed = false;
            InvokeMenuItem(explorer);
            DrainLayout();
            Assert.AreEqual(strings.Format("FileExplorerError", clickedFile.Path), errorText.Text);
            CollectionAssert.AreEqual(new[] { ("properties", clickedFile.Path), ("explorer", clickedFile.Path) }, shell.Requests);

            model.Rows[0].Marked = true;
            grid.SelectedItems.Add(model.Rows[0]);
            OpenOn(model.Rows[1]);
            Assert.HasCount(2, grid.SelectedItems);
            Assert.IsFalse(properties.IsEnabled || explorer.IsEnabled);
            Assert.IsTrue(recycle.IsEnabled);
            Assert.AreEqual(1, model.MarkedCount);

            // Keep only the unchecked file visible. The hidden file's mark still belongs to both recycle commands.
            menu.IsOpen = false;
            ((TextBox)window.FindName("FilterBox")).Text = "copy";
            ExpandResults(window);
            OpenOn(model.Rows[1]);
            Assert.AreEqual(1, model.View.Count);

            var anyEnabledDuringJob = true;
            var job = window.RunJobAsync(_ =>
            {
                anyEnabledDuringJob = properties.IsEnabled || explorer.IsEnabled || recycle.IsEnabled;
                return Task.CompletedTask;
            });
            Assert.IsTrue(job.IsCompletedSuccessfully);
            Assert.IsFalse(anyEnabledDuringJob);
            Assert.IsTrue(properties.IsEnabled && explorer.IsEnabled && recycle.IsEnabled);

            // Cancel the real confirmation after recording its proposed paths.
            string[]? proposed = null;
            _ = window.Dispatcher.InvokeAsync(() =>
            {
                var dialog = app.Windows.OfType<ReportDialog>().SingleOrDefault(report => report.Owner == window);
                if (dialog == null) return;
                try { proposed = ((ListBox)dialog.FindName("Lines")).Items.Cast<string>().ToArray(); }
                finally { dialog.Close(); }
            }, DispatcherPriority.ApplicationIdle);
            InvokeMenuItem(recycle);
            DrainLayout();
            Assert.IsNotNull(proposed);
            CollectionAssert.AreEqual(new[] { checkedFile.Path }, proposed);
            Assert.HasCount(2, model.Rows);
            Assert.AreEqual(1, model.MarkedCount);
        }
        finally { menu.IsOpen = false; }

        DataGridCell CellFor(ResultRow row) =>
            VisualChildren<DataGridCell>(grid).Single(cell => cell.Column == grid.Columns[1] && cell.DataContext == row);

        void OpenOn(ResultRow row)
        {
            menu.IsOpen = false;
            DrainLayout();
            var cell = CellFor(row);
            // Route the menu key through WPF's input manager to exercise the normal ContextMenuOpening event.
            InputManager.Current.ProcessInput(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(cell)!,
                Environment.TickCount, Key.Apps) { RoutedEvent = Keyboard.KeyUpEvent, Source = cell });
            DrainLayout();
            Assert.IsTrue(menu.IsOpen);
        }
    });
}
