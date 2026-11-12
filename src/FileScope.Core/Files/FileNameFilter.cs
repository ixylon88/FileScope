using System.Text.RegularExpressions;

namespace FileScope.Core.Files;

/// <summary>Filters candidate paths by their filename, without opening the files.</summary>
public sealed class FileNameFilter
{
    private readonly Regex? _expression;

    public bool IsActive => _expression is not null;

    public FileNameFilter(string pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);

        if (pattern.Length == 0) return;

        // Reuse one expression for the whole intake. Lookaheads support exclusions,
        // so use the backtracking engine with a finite timeout instead of NonBacktracking.
        // An app-language change must not change filename matching.
        _expression = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(100));
    }

    /// <summary>
    /// Returns matching paths and the number excluded. A cancellation or regex timeout
    /// throws before a result is returned, so the caller need not publish a partial selection.
    /// </summary>
    public (IReadOnlyList<string> Files, int Excluded) Apply(IEnumerable<string> paths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var matches = new List<string>();
        var excluded = 0;

        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(path)) continue;

            if (_expression is null || _expression.IsMatch(Path.GetFileName(path)))
                matches.Add(path);
            else
                excluded++;
        }

        cancellationToken.ThrowIfCancellationRequested();

        return (matches.AsReadOnly(), excluded);
    }
}
