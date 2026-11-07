using System.Formats.Asn1;
using FileScope.Core.Models;

namespace FileScope.Core.Signatures;

// Decoding metadata is deliberately separate from deciding whether Windows
// trusted it. Never substitute certificate NotBefore or the current clock.
/// <summary>Decodes timestamp attributes already copied out of the Windows provider state.</summary>
/// <remarks>
/// This managed ASN.1 parser performs no signature or certificate-chain verification.
/// Confirmation requires the separate Windows policy/countersigner result and a matching verification time.
/// </remarks>
internal static class AuthenticodeTimestampReader
{
    internal const string CounterSignatureOid = "1.2.840.113549.1.9.6";
    internal const string Rfc3161Oid = "1.3.6.1.4.1.311.3.3.1";
    private static readonly Asn1Tag ContextZero = new(TagClass.ContextSpecific, 0, true);

    internal static bool IsTimestamp(string oid) => oid is CounterSignatureOid or Rfc3161Oid;

    internal static DateTimeOffset? Read(string oid, ReadOnlyMemory<byte> encoded)
    {
        try
        {
            return oid switch
            {
                CounterSignatureOid => ReadCounterSignature(encoded),
                Rfc3161Oid => ReadRfc3161(encoded),
                _ => null
            };
        }
        catch (AsnContentException) { return null; }
    }

    internal static SignatureTimestamp Select(IReadOnlyList<DateTimeOffset> times,
        bool hasTimestamp, DateTimeOffset? verifiedAsOf, bool trustedCounterSigner)
    {
        if (trustedCounterSigner && verifiedAsOf.HasValue)
        {
            foreach (var time in times)
            {
                // WinVerifyTrust may reduce FILETIME precision to whole seconds.
                if (time.ToUnixTimeSeconds() == verifiedAsOf.Value.ToUnixTimeSeconds())
                    return new(time, TimestampStatus.Verified);
            }
        }

        return times.Count > 0 ? new(times[0], TimestampStatus.Unverified)
            : hasTimestamp ? SignatureTimestamp.Unreadable : SignatureTimestamp.NotPresent;
    }

    private static DateTimeOffset? ReadCounterSignature(ReadOnlyMemory<byte> encoded)
    {
        var reader = new AsnReader(encoded, AsnEncodingRules.BER);
        var signer = reader.ReadSequence();
        reader.ThrowIfNotEmpty();
        _ = signer.ReadInteger();                  // version
        _ = signer.ReadEncodedValue();             // issuer/serial or subject key identifier
        _ = signer.ReadSequence();                 // digestAlgorithm
        if (!signer.HasData || !signer.PeekTag().HasSameClassAndValue(ContextZero)) return null;
        var attributes = signer.ReadSetOf(skipSortOrderValidation: true, expectedTag: ContextZero);
        DateTimeOffset? result = null;
        while (attributes.HasData)
        {
            var attribute = attributes.ReadSequence();
            var oid = attribute.ReadObjectIdentifier();
            var values = attribute.ReadSetOf(skipSortOrderValidation: true);
            attribute.ThrowIfNotEmpty();
            if (oid != "1.2.840.113549.1.9.5") continue;
            // Ambiguous or multiple signing-time values must not be presented as verified.
            if (result.HasValue || !values.HasData) return null;
            result = values.PeekTag().HasSameClassAndValue(Asn1Tag.UtcTime)
                ? values.ReadUtcTime() : values.ReadGeneralizedTime();
            values.ThrowIfNotEmpty();
        }
        _ = signer.ReadSequence();                 // signatureAlgorithm
        _ = signer.ReadOctetString();              // signature
        if (signer.HasData)
        {
            _ = signer.ReadSetOf(true, new Asn1Tag(TagClass.ContextSpecific, 1, true));
        }

        signer.ThrowIfNotEmpty();
        return result;
    }

    private static DateTimeOffset? ReadRfc3161(ReadOnlyMemory<byte> encoded)
    {
        var reader = new AsnReader(encoded, AsnEncodingRules.BER);
        var contentInfo = reader.ReadSequence();
        reader.ThrowIfNotEmpty();
        if (contentInfo.ReadObjectIdentifier() != "1.2.840.113549.1.7.2") return null;
        var wrapper = contentInfo.ReadSequence(ContextZero);
        contentInfo.ThrowIfNotEmpty();
        var signedData = wrapper.ReadSequence();
        wrapper.ThrowIfNotEmpty();
        _ = signedData.ReadInteger();
        _ = signedData.ReadSetOf(skipSortOrderValidation: true);
        var content = signedData.ReadSequence();
        if (content.ReadObjectIdentifier() != "1.2.840.113549.1.9.16.1.4") return null;
        var explicitContent = content.ReadSequence(ContextZero);
        content.ThrowIfNotEmpty();
        var tstBytes = explicitContent.ReadOctetString();
        explicitContent.ThrowIfNotEmpty();
        var tstReader = new AsnReader(tstBytes, AsnEncodingRules.BER);
        var tst = tstReader.ReadSequence();
        tstReader.ThrowIfNotEmpty();
        _ = tst.ReadInteger();                     // version
        _ = tst.ReadObjectIdentifier();            // policy
        _ = tst.ReadSequence();                    // messageImprint
        _ = tst.ReadInteger();                     // serialNumber
        return tst.ReadGeneralizedTime();          // genTime; optional fields may follow
    }
}
