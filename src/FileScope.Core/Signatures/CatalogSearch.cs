using FileScope.Core.Models;

namespace FileScope.Core.Signatures;

/// <summary>Shares one snapshot of supplied catalogs across all files in an analysis batch.</summary>
public sealed class CatalogSearch(IEnumerable<string> paths)
{
    private readonly string[] _paths = paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    private readonly Lock _gate = new();
    private WindowsCatalogIndex? _index;
    private IReadOnlyList<string> _indexIssues = [];

    /// <summary>Recognizes a possible catalog by filename during file intake.</summary>
    public static bool IsCatalogPath(string path) => path.EndsWith(".cat", StringComparison.OrdinalIgnoreCase);

    /// <summary>Builds the index on first use, on the analysis worker. A cancelled build can be retried.</summary>
    public CatalogLookup Inspect(FileStream file, RevocationMode mode, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows()) return new([], ["Windows catalog verification requires Windows."], false);

        WindowsCatalogIndex index;
        lock (_gate)
        {
            if (_index is null)
            {
                var issues = new List<string>();
                _index = new WindowsCatalogIndex(_paths, (path, error) => issues.Add($"{path}: {error}"), token);
                _indexIssues = issues.AsReadOnly();
            }

            index = _index;
        }

        return index.Inspect(file, mode, token) with { IndexIssues = _indexIssues };
    }
}
