namespace FileScope.Core.Export;

public enum CsvDelimiter { Semicolon, Comma }
public enum ResultExportFormat { Csv, Json }

public sealed record ResultExportOptions(
    ResultExportFormat Format = ResultExportFormat.Csv, CsvDelimiter CsvDelimiter = CsvDelimiter.Semicolon);

public sealed record ResultExportColumn(string Id, string Header);

// A presentation snapshot: stable IDs for JSON, localized headers for CSV.
public sealed record ResultExportTable(
    IReadOnlyList<ResultExportColumn> Columns, IReadOnlyList<IReadOnlyList<string>> Rows);

public interface IResultExporter
{
    Task ExportAsync(string path, ResultExportTable table, ResultExportOptions? options = null,
        CancellationToken cancellationToken = default);
}

// Strategies only serialize. The export service owns files, replacement and cleanup.
public interface IResultExportStrategy
{
    ResultExportFormat Format { get; }
    Task WriteAsync(Stream destination, ResultExportTable table, ResultExportOptions options,
        CancellationToken cancellationToken);
}
