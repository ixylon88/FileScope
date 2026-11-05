namespace FileScope.Core.Export;

public sealed class ResultExporter(IEnumerable<IResultExportStrategy> strategies) : IResultExporter
{
    private readonly Dictionary<ResultExportFormat, IResultExportStrategy> _strategies =
        strategies.ToDictionary(strategy => strategy.Format);

    public async Task ExportAsync(string path, ResultExportTable table, ResultExportOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(table);
        options ??= new ResultExportOptions();
        if (!_strategies.TryGetValue(options.Format, out var strategy))
            throw new ArgumentOutOfRangeException(nameof(options), "Unsupported export format.");
        if (table.Columns.Count == 0) throw new ArgumentException("Select at least one export column.", nameof(table));
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var column in table.Columns)
        {
            if (string.IsNullOrWhiteSpace(column.Id) || !ids.Add(column.Id))
                throw new ArgumentException("Export column IDs must be nonempty and unique.", nameof(table));
        }
        cancellationToken.ThrowIfCancellationRequested();
        var destination = Path.GetFullPath(path);
        var temporary = Path.Combine(Path.GetDirectoryName(destination)!, $".filescope-export-{Guid.NewGuid():N}.tmp");
        try
        {
            // Both strategies replace an existing destination only after success.
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                65536, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await strategy.WriteAsync(stream, table, options, cancellationToken).ConfigureAwait(false);
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
}
