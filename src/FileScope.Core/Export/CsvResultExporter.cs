using System.Text;

namespace FileScope.Core.Export;

public sealed class CsvResultExporter : IResultExporter
{
    public async Task ExportAsync(string path, ResultExportTable table, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(table);
        if (table.Headers.Count == 0) throw new ArgumentException("Select at least one export column.", nameof(table));
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
                await WriteRecordAsync(writer, table.Headers, cancellationToken).ConfigureAwait(false);
                foreach (var row in table.Rows)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (row.Count != table.Headers.Count)
                        throw new ArgumentException("Every row must have one value per header.", nameof(table));
                    await WriteRecordAsync(writer, row, cancellationToken).ConfigureAwait(false);
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

    private static Task WriteRecordAsync(StreamWriter writer, IReadOnlyList<string> values, CancellationToken token)
    {
        // Quote every value: commas, CR/LF, quotes and Unicode round-trip exactly.
        var record = string.Join(",", values.Select(value => "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""));
        return writer.WriteLineAsync(record.AsMemory(), token);
    }
}
