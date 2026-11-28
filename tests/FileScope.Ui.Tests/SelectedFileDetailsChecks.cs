using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace FileScope.Ui.Tests;

internal sealed class SelectedFileDetailsChecks
{
    public SelectedFileDetailsChecks(Window window)
    {
        var panel = (SelectedFileDetails)window.FindName("DetailsPanel");
        Expander = (Expander)panel.FindName("DetailsExpander");
        Label = (TextBlock)panel.FindName("DetailsLabel");
        Path = (TextBlock)panel.FindName("DetailsPath");
        Editor = (TextBox)panel.FindName("DetailsBox");
    }

    public Expander Expander { get; }
    public TextBlock Label { get; }
    public TextBlock Path { get; }
    public TextBox Editor { get; }

    public void CheckSelectionAndCopy(DataGrid grid, object first, object second, string expectedPath, string expectedText)
    {
        var strings = LocalizationService.Instance;
        Assert.IsFalse(Expander.IsExpanded);
        Expander.IsExpanded = true;
        grid.SelectedItem = first;
        grid.CurrentCell = new DataGridCellInfo(first, grid.Columns[1]);
        Expander.UpdateLayout();

        Assert.AreEqual(expectedPath, Path.Text);
        Assert.AreEqual(expectedPath, Path.ToolTip);
        Assert.AreEqual(strings["DetailsTitle"], Label.Text);
        Assert.AreEqual(strings["DetailsHint"], Editor.ToolTip);
        Assert.IsTrue(Path.Margin.Left >= 16);
        Assert.IsTrue(Editor.IsReadOnly);

        Editor.Focus();
        grid.CurrentCell = new DataGridCellInfo();
        Editor.SelectAll();
        Assert.AreEqual(expectedText, Editor.SelectedText);
        Assert.IsTrue(ApplicationCommands.Copy.CanExecute(null, Editor));
        Assert.AreSame(first, grid.SelectedItem);

        Expander.IsExpanded = false;
        Expander.IsExpanded = true;
        Assert.AreEqual(expectedText, Editor.Text);

        grid.SelectedItems.Add(second);
        Assert.AreEqual("", Path.Text);
        Assert.AreEqual("", Editor.Text);

        grid.UnselectAll();
        Assert.AreEqual("", Path.Text);
        Assert.AreEqual("", Editor.Text);
    }
}
