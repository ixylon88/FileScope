using FileScope.Core.Signatures;

namespace FileScope.Core.Tests;

[TestClass]
public sealed class SignatureDigestTests
{
    [TestMethod]
    public void KnownCatalogOidsSupplyTheSignerLabelWindowsNameAndBufferSize()
    {
        (string Oid, string Label, string WindowsName, int Bytes)[] cases =
        [
            ("1.3.14.3.2.26", "SHA-1", "SHA1", 20),
            ("2.16.840.1.101.3.4.2.1", "SHA-256", "SHA256", 32),
            ("2.16.840.1.101.3.4.2.2", "SHA-384", "SHA384", 48),
            ("2.16.840.1.101.3.4.2.3", "SHA-512", "SHA512", 64)
        ];

        foreach (var (oid, label, windowsName, bytes) in cases)
        {
            var digest = SignatureDigest.ForCatalog(oid);
            Assert.AreEqual(label, SignatureDigest.SignerName(oid));
            Assert.AreEqual(windowsName, digest.WindowsName);
            Assert.AreEqual(bytes, digest.ByteLength);
        }
    }

    [TestMethod]
    public void SignerMetadataToleratesOidsOutsideTheCatalogPolicy()
    {
        (string? Oid, string Label)[] cases =
            [("1.2.840.113549.2.5", "MD5"), ("1.2.3.4", "1.2.3.4"), ("", ""), (null, "")];

        foreach (var (oid, label) in cases)
        {
            Assert.AreEqual(label, SignatureDigest.SignerName(oid));
            Assert.ThrowsExactly<InvalidDataException>(() => SignatureDigest.ForCatalog(oid));
        }
    }
}
