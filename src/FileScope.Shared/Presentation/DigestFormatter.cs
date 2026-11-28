using System.Buffers.Text;
using System.Collections.ObjectModel;
using FileScope.Core.Models;

namespace FileScope.Shared.Presentation;

public enum HashFormat { HexUpper, HexLower, HexColon, Base64, Base64Url }

// Presentation only: the analysis result always retains its original hex digest.
public static class DigestFormatter
{
    /// <summary>Formats a completed set of file checksums for display in either application.</summary>
    public static IReadOnlyDictionary<ChecksumAlgorithm, string> FormatAll(
        IReadOnlyDictionary<ChecksumAlgorithm, string>? hashes, HashFormat format)
    {
        if (!Enum.IsDefined(format)) throw new ArgumentOutOfRangeException(nameof(format));
        if (hashes is null || hashes.Count == 0) return ReadOnlyDictionary<ChecksumAlgorithm, string>.Empty;

        // The core publishes read-only snapshots. Keep one copy when it already has the requested format.
        if (format == HashFormat.HexUpper && hashes is ReadOnlyDictionary<ChecksumAlgorithm, string>
            && hashes.Values.All(IsUpperHex))
        {
            return hashes;
        }

        return hashes.ToDictionary(pair => pair.Key, pair => Format(pair.Value, format));
    }

    public static string Format(string? hex, HashFormat format)
    {
        if (!Enum.IsDefined(format)) throw new ArgumentOutOfRangeException(nameof(format));
        if (string.IsNullOrEmpty(hex)) return "";
        if (format == HashFormat.HexUpper && IsUpperHex(hex)) return hex;

        var bytes = Convert.FromHexString(hex);
        return format switch
        {
            HashFormat.HexUpper => Convert.ToHexString(bytes),
            HashFormat.HexLower => Convert.ToHexStringLower(bytes),
            HashFormat.HexColon => string.Join(":", bytes.Select(value => value.ToString("X2"))),
            HashFormat.Base64 => Convert.ToBase64String(bytes),
            HashFormat.Base64Url => Base64Url.EncodeToString(bytes),
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };
    }

    private static bool IsUpperHex(string? hex) =>
        hex is not null && hex.Length % 2 == 0 && hex.All(char.IsAsciiHexDigitUpper);
}
