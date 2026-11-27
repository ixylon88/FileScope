using System.Windows.Controls;

namespace FileScope.Shared.Controls;

public static class PathColumnWidth
{
    public static void Apply(DataGridColumn column, bool automatic)
    {
        // Paths may use their full width. Bounded mode leaves space for neighbouring columns.
        column.MaxWidth = automatic ? double.PositiveInfinity : 600;
        column.Width = automatic ? DataGridLength.Auto : new DataGridLength(320);
    }
}
