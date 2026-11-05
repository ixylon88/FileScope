using System.Text.Json;

namespace FileScope.Core.Export;

public sealed class JsonExportStrategy : IResultExportStrategy
{
    public ResultExportFormat Format => ResultExportFormat.Json;

    public async Task WriteAsync(Stream destination, ResultExportTable table, ResultExportOptions options,
        CancellationToken cancellationToken)
    {
        // UTF-8 without BOM. Flush in batches to bound the writer's buffer.
        await using var writer = new Utf8JsonWriter(destination, new JsonWriterOptions { Indented = true });
        writer.WriteStartArray();
        foreach (var row in table.Rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (row.Count != table.Columns.Count)
                throw new ArgumentException("Every row must have one value per column.", nameof(table));
            writer.WriteStartObject();
            for (var column = 0; column < table.Columns.Count; column++)
                writer.WriteString(table.Columns[column].Id, row[column]);
            writer.WriteEndObject();
            if (writer.BytesPending >= 65536)
                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        writer.WriteEndArray();
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
