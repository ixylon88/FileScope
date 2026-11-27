using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace FileScope.Shared;

/// <summary>Common Explorer actions, preceded by the application's own file command.</summary>
public partial class FileRowContextMenu : ContextMenu
{
    public FileRowContextMenu() => InitializeComponent();

    public MenuItem? LeadingAction
    {
        get;
        set
        {
            if (field != null) Items.Remove(field);
            field = value;

            if (value == null)
            {
                ActionSeparator.Visibility = Visibility.Collapsed;
                return;
            }

            Items.Insert(0, value);
            ActionSeparator.SetBinding(VisibilityProperty, new Binding(nameof(Visibility)) { Source = value });
        }
    }

    // Opening the menu on an already selected row preserves the entire selection.
    public static void SelectTargetRow(DataGrid grid, DataGridRow row)
    {
        if (row.IsSelected) return;

        grid.UnselectAll();
        row.IsSelected = true;
    }
}
