using FileScope.App.ViewModels;
using FileScope.Core.Export;

namespace FileScope.App.Presentation;

public static class ResultExportProjection
{
    public static ResultExportTable Capture(IEnumerable<FileRowViewModel> rows, IEnumerable<ColumnOption> columns)
    {
        var selected = columns.Where(column => column.IsExportSelected).ToArray();
        if (selected.Length == 0) throw new InvalidOperationException("Select at least one export column.");
        // Capture on the UI thread before writing in the background. Later changes
        // to language, digest representation or selected signature cannot mix values.
        var fields = Array.AsReadOnly(selected.Select(column => new ResultExportColumn(column.Id, column.Label)).ToArray());
        var values = rows.Select(row => (IReadOnlyList<string>)Array.AsReadOnly(
            selected.Select(column => column.GetExportValue(row)).ToArray())).ToArray();
        return new ResultExportTable(fields, Array.AsReadOnly(values));
    }
}
