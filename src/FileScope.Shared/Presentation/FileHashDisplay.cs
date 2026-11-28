using FileScope.Core.Models;

namespace FileScope.Shared.Presentation;

/// <summary>Formats a row's checksum snapshot on first use. Replace the value when its source or format changes.</summary>
public struct FileHashDisplay(IReadOnlyDictionary<ChecksumAlgorithm, string>? hashes, HashFormat format)
{
    public HashFormat Format { get; } = Enum.IsDefined(format) ? format : throw new ArgumentOutOfRangeException(nameof(format));

    public IReadOnlyDictionary<ChecksumAlgorithm, string> Formatted =>
        field ??= DigestFormatter.FormatAll(hashes, Format);

    public bool Matches(string query)
    {
        if (hashes is not { Count: > 0 }) return false;
        if (DigestSearch.Contains(hashes.Values, query, StringComparison.OrdinalIgnoreCase)) return true;

        return DigestSearch.DisplayComparison(Format) is { } comparison
            && DigestSearch.Contains(Formatted.Values, query, comparison);
    }
}
