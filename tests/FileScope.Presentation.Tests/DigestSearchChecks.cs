using System.Collections;
using FileScope.Core.Models;

namespace FileScope.Presentation.Tests;

// Both products run these searches through their own row or collection filter.
internal static class DigestSearchChecks
{
    internal const string Hex = "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD";

    internal sealed class CountingChecksums(string hex = Hex) : IReadOnlyDictionary<ChecksumAlgorithm, string>
    {
        private readonly Dictionary<ChecksumAlgorithm, string> _source = new() { [ChecksumAlgorithm.SHA256] = hex };
        public int Enumerations { get; private set; }
        public int Count => _source.Count;
        public IEnumerable<ChecksumAlgorithm> Keys => _source.Keys;
        public IEnumerable<string> Values => _source.Values;
        public string this[ChecksumAlgorithm key] => _source[key];
        public bool ContainsKey(ChecksumAlgorithm key) => _source.ContainsKey(key);
        public bool TryGetValue(ChecksumAlgorithm key, out string value) => _source.TryGetValue(key, out value!);

        public IEnumerator<KeyValuePair<ChecksumAlgorithm, string>> GetEnumerator()
        {
            Enumerations++;
            return _source.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    internal static void CheckDeferredFormatting(CountingChecksums source, Action<HashFormat> selectFormat,
        Func<string, bool> matches, Func<string> cell, Func<string> details)
    {
        Assert.AreEqual(0, source.Enumerations);
        Assert.IsTrue(matches("8f01cfea"));
        Assert.AreEqual(0, source.Enumerations, "A canonical hex match needs no Base64 text.");

        Assert.IsTrue(matches("+pBQUDe"));
        var base64 = Convert.ToBase64String(Convert.FromHexString(Hex));
        Assert.AreEqual(base64, cell());
        Assert.Contains(base64, details());
        Assert.AreEqual(1, source.Enumerations, "Search, cell and details share one conversion.");

        Assert.IsTrue(matches(""));
        selectFormat(HashFormat.HexLower);
        Assert.IsFalse(matches("no such hash"));
        Assert.IsTrue(matches(""));
        selectFormat(HashFormat.Base64Url);
        Assert.AreEqual(1, source.Enumerations, "Changing formats and searching plain hex leave the display deferred.");

        Assert.AreEqual(DigestFormatter.Format(Hex, HashFormat.Base64Url), cell());
        Assert.IsTrue(matches("-pBQUDe"));
        Assert.AreEqual(2, source.Enumerations);
    }

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
