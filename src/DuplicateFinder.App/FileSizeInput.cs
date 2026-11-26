using System.Globalization;

namespace DuplicateFinder.App;

public enum FileSizeUnit { Bytes = 1, Kilobytes = 1_000, Megabytes = 1_000_000, Gigabytes = 1_000_000_000 }

internal static class FileSizeInput
{
    internal static IReadOnlyDictionary<FileSizeUnit, string> Units { get; } = new Dictionary<FileSizeUnit, string>
    {
        [FileSizeUnit.Bytes] = "B",
        [FileSizeUnit.Kilobytes] = "kB",
        [FileSizeUnit.Megabytes] = "MB",
        [FileSizeUnit.Gigabytes] = "GB"
    };

    internal static string Format(long? bytes, FileSizeUnit unit, CultureInfo culture) =>
        (bytes / (decimal)unit)?.ToString("0.#########", culture) ?? "";

    internal static bool TryParse(string text, FileSizeUnit unit, CultureInfo culture, out long? bytes)
    {
        bytes = null;
        var value = text.AsSpan().Trim();
        if (value.IsEmpty) return true;

        // No SI unit offered here can turn a nonzero digit beyond nine decimal places
        // into a whole byte. Check before decimal.TryParse could round a very long input.
        var separator = culture.NumberFormat.NumberDecimalSeparator;
        var point = value.IndexOf(separator, StringComparison.Ordinal);
        if (point >= 0 && value[(point + separator.Length)..].TrimEnd('0').Length > 9) return false;

        if (!decimal.TryParse(value, NumberStyles.AllowDecimalPoint, culture, out var amount)
            || amount > long.MaxValue / (decimal)unit)
        {
            return false;
        }

        var scaled = amount * (decimal)unit;
        if (scaled != decimal.Truncate(scaled)) return false;

        bytes = (long)scaled;
        return true;
    }
}
