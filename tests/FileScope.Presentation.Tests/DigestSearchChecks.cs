namespace FileScope.Presentation.Tests;

// Both products run these searches through their own row or collection filter.
internal static class DigestSearchChecks
{
    internal const string Hex = "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD";

    internal static void CheckFileHash(Action<HashFormat> selectFormat, Func<string, bool> matches)
    {
        foreach (var format in Enum.GetValues<HashFormat>())
        {
            selectFormat(format);
            Assert.IsTrue(matches("8f01cfea"), $"Canonical hex remains searchable in {format}.");
        }

        selectFormat(HashFormat.HexColon);
        Assert.IsTrue(matches("bf:8f:01"));

        selectFormat(HashFormat.Base64);
        Assert.IsTrue(matches("+pBQUDe"));
        Assert.IsFalse(matches("+pbqude"));

        selectFormat(HashFormat.Base64Url);
        Assert.IsTrue(matches("-pBQUDe"));
        Assert.IsFalse(matches("-pbqude"));
        Assert.IsFalse(matches("+pBQUDe"), "The search follows the selected alphabet.");
    }
}
