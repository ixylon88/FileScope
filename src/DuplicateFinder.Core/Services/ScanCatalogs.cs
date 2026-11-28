using System.Runtime.Versioning;
using DuplicateFinder.Core.Abstractions;
using DuplicateFinder.Core.Files;
using DuplicateFinder.Core.Models;
using FileScope.Core.Models;
using FileScope.Core.Signatures;

namespace DuplicateFinder.Core.Services;

/// <summary>Collects loose catalog paths and builds one index for the first byte-confirmed group.</summary>
internal sealed class ScanCatalogs(IFileFacts facts, List<ScanIssue> issues, IProgress<ScanProgress>? progress)
{
    private Dictionary<string, FileKey?>? _paths;
    private WindowsCatalogIndex? _index;
    private bool _unreadablePath;

    internal void Remember(string path, FileKey? key = null)
    {
        _paths ??= new(StringComparer.Ordinal);
        if (key.HasValue) _paths[path] = key;
        else _paths.TryAdd(path, null);
    }

    [SupportedOSPlatform("windows")]
    internal CatalogLookup Inspect(FileStream file, RevocationMode mode, CancellationToken token)
    {
        _index ??= new WindowsCatalogIndex(PathsToIndex(token), (path, reason) => issues.Add(new(path, reason)), token);
        progress?.Report(new("Catalog", file.Name, 0, 0));

        var result = _index.Inspect(file, mode, token);
        return _unreadablePath ? result with { IndexComplete = false } : result;
    }

    private IEnumerable<string> PathsToIndex(CancellationToken token)
    {
        if (_paths is null) yield break;

        var seen = new HashSet<FileKey>();
        var index = 0;
        foreach (var (path, knownKey) in _paths)
        {
            token.ThrowIfCancellationRequested();
            progress?.Report(new("CatalogIndex", path, index++, _paths.Count));

            FileKey key;
            try
            {
                FileAccessChecks.CheckPath(path);
                key = knownKey ?? ReadKey(path);
            }
            catch (Exception ex) when (FileAccessChecks.Expected(ex))
            {
                issues.Add(new(path, ex.Message));
                _unreadablePath = true;
                continue;
            }

            if (seen.Add(key)) yield return path;
        }
    }

    private FileKey ReadKey(string path)
    {
        using var input = FileAccessChecks.Open(path);
        return facts.Read(input).Key;
    }
}
