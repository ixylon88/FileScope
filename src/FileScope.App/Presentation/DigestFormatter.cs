using System.Buffers.Text;

namespace FileScope.App.Presentation;

public enum HashFormat { HexUpper, HexLower, HexColon, Base64, Base64Url }

// Presentation only: the analysis result always retains its original hex digest.
public static class DigestFormatter
{
    public static string Format(string? hex, HashFormat format)
    {
        if (!Enum.IsDefined(format)) throw new ArgumentOutOfRangeException(nameof(format));
        if (string.IsNullOrEmpty(hex)) return "";
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
}
