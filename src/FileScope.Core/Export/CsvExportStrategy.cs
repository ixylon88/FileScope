using System.Text;

namespace FileScope.Core.Export;

public sealed class CsvExportStrategy : IResultExportStrategy
{
    public ResultExportFormat Format => ResultExportFormat.Csv;

    public async Task WriteAsync(Stream destination, ResultExportTable table, ResultExportOptions options,
        CancellationToken cancellationToken)
    {
        var separator = options.CsvDelimiter switch
        {
            CsvDelimiter.Semicolon => ";",
            CsvDelimiter.Comma => ",",
            _ => throw new ArgumentOutOfRangeException(nameof(options), options.CsvDelimiter, "Unsupported CSV delimiter.")
        };
        await using var writer = new StreamWriter(destination,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), leaveOpen: true);
        writer.NewLine = "\r\n";
        await WriteRecordAsync(writer, table.Columns.Select(column => column.Header), separator, cancellationToken).ConfigureAwait(false);
        foreach (var row in table.Rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (row.Count != table.Columns.Count)
                throw new ArgumentException("Every row must have one value per column.", nameof(table));
            await WriteRecordAsync(writer, row, separator, cancellationToken).ConfigureAwait(false);
        }
    }

    private static Task WriteRecordAsync(StreamWriter writer, IEnumerable<string> values, string separator,
        CancellationToken token)
    {
        // Quote every value: either delimiter, CR/LF, quotes and Unicode round-trip exactly.
        var record = string.Join(separator, values.Select(value => "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""));
        return writer.WriteLineAsync(record.AsMemory(), token);
    }
}
