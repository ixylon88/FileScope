using DuplicateFinder.Core.Models;

namespace DuplicateFinder.Core.Files;

/// <summary>Compares directory spellings during one Windows scan, including case-sensitive NTFS directories.</summary>
internal sealed class DirectoryPathComparer(Func<string, FileKey?> readKey) : IEqualityComparer<string>
{
    private Dictionary<string, FileKey?>? _keys;

    public bool Equals(string? first, string? second)
    {
        if (StringComparer.Ordinal.Equals(first, second)) return true;
        if (first is null || second is null || !StringComparer.OrdinalIgnoreCase.Equals(first, second)) return false;

        // Only a case-only match needs handles. If identity is unavailable, visit both paths.
        return Key(first) is { } key && Key(second) == key;
    }

    public int GetHashCode(string path) => StringComparer.OrdinalIgnoreCase.GetHashCode(path);

    private FileKey? Key(string path)
    {
        _keys ??= new(StringComparer.Ordinal);
        if (_keys.TryGetValue(path, out var key)) return key;

        key = readKey(path);
        _keys.Add(path, key);
        return key;
    }
}
