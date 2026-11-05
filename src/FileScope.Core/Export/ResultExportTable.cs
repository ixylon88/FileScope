namespace FileScope.Core.Export;

// A presentation snapshot: the writer knows neither WPF nor analysis models.
// Other export strategies can consume the same ordered headers and values.
public sealed record ResultExportTable(
    IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows);

public interface IResultExporter
{
    Task ExportAsync(string path, ResultExportTable table, CancellationToken cancellationToken = default);
}
