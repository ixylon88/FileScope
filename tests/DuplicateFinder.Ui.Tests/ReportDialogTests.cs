using System.Windows;
using System.Windows.Controls;
using DuplicateFinder.App.ViewModels;
using FileScope.Ui.Tests;

namespace DuplicateFinder.Ui.Tests;

public sealed partial class WindowTests
{
    [TestMethod]
    public Task ReportVirtualizesLongListsAndRequiresExplicitConfirmation() => WpfTestHost.RunAsync((app, strings) =>
    {
        var owner = ShowResults(new ResultsViewModel(), new ThemeService(app));
        var paths = Enumerable.Range(0, 2000).Select(index => $"archive/file-{index}.bin").ToArray();

        var report = ReportDialog.ForLines("Recycle", s => s["Diagnostics"], _ => paths);
        report.Owner = owner;
        report.Show();
        report.UpdateLayout();
        var lines = (ListBox)report.FindName("Lines");
        Assert.AreEqual(paths.Length, lines.Items.Count);
        Assert.IsTrue(VirtualizingPanel.GetIsVirtualizing(lines));
        Assert.AreEqual(VirtualizationMode.Recycling, VirtualizingPanel.GetVirtualizationMode(lines));
        Assert.IsNotNull(lines.ItemContainerGenerator.ContainerFromIndex(0));
        Assert.IsNull(lines.ItemContainerGenerator.ContainerFromIndex(paths.Length - 1));
        Assert.IsFalse(report.ConfirmCommand.CanExecute(null));
        Assert.AreEqual(Visibility.Collapsed, ((Button)report.FindName("ConfirmButton")).Visibility);
        report.Close();

        var prompt = ReportDialog.ForLines("Recycle", s => s.Format("ManualPrompt", paths.Length), _ => paths, confirmationKey: "Recycle");
        prompt.Owner = owner;
        var action = (Button)prompt.FindName("ConfirmButton");
        var cancel = (Button)prompt.FindName("CloseButton");
        var hasExplicitChoice = false;
        prompt.ContentRendered += (_, _) =>
        {
            hasExplicitChoice = action.IsVisible && action.IsEnabled && !action.IsDefault && cancel.IsCancel
                && Equals(action.Content, strings["Recycle"]) && Equals(cancel.Content, strings["Cancel"]);
            if (hasExplicitChoice) action.Command.Execute(null);
            else prompt.Close();
        };

        Assert.IsTrue(prompt.ShowDialog());
        Assert.IsTrue(hasExplicitChoice);
    });
}
