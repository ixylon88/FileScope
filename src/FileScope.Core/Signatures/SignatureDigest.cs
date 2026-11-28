namespace FileScope.Core.Signatures;

/// <summary>Maps Authenticode digest OIDs to signer labels and Windows catalog hash parameters.</summary>
internal sealed record SignatureDigest(string DisplayName, string WindowsName, int ByteLength, bool SupportsCatalog = true)
{
    // Windows checks these two algorithms even when no loose catalog supplied a member.
    internal static SignatureDigest Sha1 { get; } = new("SHA-1", "SHA1", 20);
    internal static SignatureDigest Sha256 { get; } = new("SHA-256", "SHA256", 32);

    private static readonly Dictionary<string, SignatureDigest> ByOid = new(StringComparer.Ordinal)
    {
        ["1.2.840.113549.2.5"] = new("MD5", "MD5", 16, SupportsCatalog: false),
        ["1.3.14.3.2.26"] = Sha1,
        ["2.16.840.1.101.3.4.2.1"] = Sha256,
        ["2.16.840.1.101.3.4.2.2"] = new("SHA-384", "SHA384", 48),
        ["2.16.840.1.101.3.4.2.3"] = new("SHA-512", "SHA512", 64)
    };

    internal static string SignerName(string? oid) => Find(oid)?.DisplayName ?? oid ?? "";

    internal static SignatureDigest ForCatalog(string? oid) => Find(oid) is { SupportsCatalog: true } digest
        ? digest : throw new InvalidDataException("Unsupported catalog member digest algorithm.");

    private static SignatureDigest? Find(string? oid) => oid is null ? null : ByOid.GetValueOrDefault(oid);
}
