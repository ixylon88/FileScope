using System.Text;

namespace FileScope.Core.Export;

public sealed class CsvResultExporter : IResultExporter
{
    public async Task ExportAsync(string path, ResultExportTable table, CsvDelimiter delimiter = CsvDelimiter.Semicolon,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(table);
        if (table.Headers.Count == 0) throw new ArgumentException("Select at least one export column.", nameof(table));
        var separator = delimiter switch
        {
            CsvDelimiter.Semicolon => ";",
            CsvDelimiter.Comma => ",",
            _ => throw new ArgumentOutOfRangeException(nameof(delimiter), delimiter, "Unsupported CSV delimiter.")
        };
        cancellationToken.ThrowIfCancellationRequested();
        var destination = Path.GetFullPath(path);
        var temporary = Path.Combine(Path.GetDirectoryName(destination)!, $".filescope-export-{Guid.NewGuid():N}.tmp");
        try
        {
            // Write beside the destination, then replace it only after a complete
            // successful export. Cancellation/errors leave an existing file intact.
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                65536, FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)))
            {
                writer.NewLine = "\r\n";
                await WriteRecordAsync(writer, table.Headers, separator, cancellationToken).ConfigureAwait(false);
                foreach (var row in table.Rows)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (row.Count != table.Headers.Count)
                        throw new ArgumentException("Every row must have one value per header.", nameof(table));
                    await WriteRecordAsync(writer, row, separator, cancellationToken).ConfigureAwait(false);
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (IOException) { /* Preserve the original export error. */ }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static Task WriteRecordAsync(StreamWriter writer, IReadOnlyList<string> values, string separator,
        CancellationToken token)
    {
        // Quote every value: either delimiter, CR/LF, quotes and Unicode round-trip exactly.
        var record = string.Join(separator, values.Select(value => "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""));
        return writer.WriteLineAsync(record.AsMemory(), token);
    }
}
