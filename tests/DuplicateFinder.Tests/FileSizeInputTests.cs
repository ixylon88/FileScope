using System.Globalization;
using System.Text.Json;
using DuplicateFinder.App;
using DuplicateFinder.Core;

namespace DuplicateFinder.Tests;

[TestClass]
public sealed class FileSizeInputTests
{
    [TestMethod]
    public void BoundsAcceptOnlyNonNegativeOrderedByteCounts()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FileSizeRange(-1, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FileSizeRange(null, -1));
        Assert.Throws<ArgumentException>(() => new FileSizeRange(2, 1));
        Assert.IsTrue(new FileSizeRange(long.MaxValue, null).Includes(long.MaxValue));

        var minimumOnly = new FileSizeRange(200_000, null);
        Assert.AreEqual(minimumOnly, JsonSerializer.Deserialize<FileSizeRange>(JsonSerializer.Serialize(minimumOnly)));
    }

    [TestMethod]
    public void LocalizedUnitsRoundTripExactlyToWholeBytes()
    {
        var de = CultureInfo.GetCultureInfo("de");
        Assert.IsTrue(FileSizeInput.TryParse(" 1,25 ", FileSizeUnit.Kilobytes, de, out var bytes));
        Assert.AreEqual(1_250L, bytes);
        Assert.IsTrue(FileSizeInput.TryParse("0.000000001", FileSizeUnit.Gigabytes, CultureInfo.InvariantCulture, out bytes));
        Assert.AreEqual(1L, bytes);
        Assert.IsTrue(FileSizeInput.TryParse(" ", FileSizeUnit.Bytes, de, out bytes));
        Assert.IsNull(bytes);

        foreach (var unit in Enum.GetValues<FileSizeUnit>())
        {
            foreach (var value in new[] { 0L, 1L, 1_500_001L, long.MaxValue })
            {
                var display = FileSizeInput.Format(value, unit, de);
                Assert.IsTrue(FileSizeInput.TryParse(display, unit, de, out bytes), display);
                Assert.AreEqual(value, bytes);
            }
        }
    }

    [TestMethod]
    public void InvalidSubByteAndOverflowInputsAreRejected()
    {
        var en = CultureInfo.GetCultureInfo("en");
        Assert.IsFalse(FileSizeInput.TryParse("-1", FileSizeUnit.Bytes, en, out _));
        Assert.IsFalse(FileSizeInput.TryParse("many", FileSizeUnit.Megabytes, en, out _));
        Assert.IsFalse(FileSizeInput.TryParse("1,5", FileSizeUnit.Megabytes, en, out _));
        Assert.IsFalse(FileSizeInput.TryParse("0.0001", FileSizeUnit.Kilobytes, en, out _));
        Assert.IsFalse(FileSizeInput.TryParse("1.0000000000000000000000000000001", FileSizeUnit.Gigabytes, en, out _));
        Assert.IsFalse(FileSizeInput.TryParse("9223372036.854775808", FileSizeUnit.Gigabytes, en, out _));
    }
}
