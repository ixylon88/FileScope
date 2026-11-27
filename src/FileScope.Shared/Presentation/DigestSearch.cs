namespace FileScope.Shared.Presentation;

public static class DigestSearch
{
    public static bool MatchesAny(IEnumerable<string> hex, IEnumerable<string> displayed, HashFormat format, string query)
    {
        if (hex.Any(value => value.Contains(query, StringComparison.OrdinalIgnoreCase))) return true;

        return DisplayComparison(format) is { } comparison
            && displayed.Any(value => value.Contains(query, comparison));
    }

    public static bool Matches(string? hex, string displayed, HashFormat format, string query)
    {
        if (hex?.Contains(query, StringComparison.OrdinalIgnoreCase) == true) return true;

        return DisplayComparison(format) is { } comparison && displayed.Contains(query, comparison);
    }

    // Plain hex in either letter case is already covered by the canonical value.
    private static StringComparison? DisplayComparison(HashFormat format) => format switch
    {
        HashFormat.HexUpper or HashFormat.HexLower => null,
        HashFormat.HexColon => StringComparison.OrdinalIgnoreCase,
        HashFormat.Base64 or HashFormat.Base64Url => StringComparison.Ordinal,
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };
}
