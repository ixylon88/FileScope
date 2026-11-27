using System.Collections.ObjectModel;

namespace FileScope.Core.Models;

public enum ChecksumAlgorithm { MD5, SHA1, SHA256, SHA384, SHA512 }
public enum RevocationMode { OfflineCache, Online }
public enum AnalysisStage { Reading, CheckingSignature, ReadingMetadata, OpeningFile, ReadingPeHeader }
public enum AnalysisError { None, InvalidPath, DirectoryNotSupported, NotFound, AccessDenied, ReadFailure, AnalysisFailure }
public enum SignatureStatus
{
    NotChecked, Valid, NoEmbeddedSignature, InvalidSignature, Untrusted,
    Expired, Revoked, RevocationUnknown, UnsupportedFormat, UnsupportedPlatform, CheckFailed
}

// Copies the selection so subsequent UI changes cannot alter an active batch.
public sealed class AnalysisOptions
{
    public IReadOnlyList<ChecksumAlgorithm> Algorithms { get; }
    public bool InspectSignature { get; }
    public RevocationMode RevocationMode { get; }
    public bool InspectFileProperties { get; }
    public bool InspectPeHeader { get; }

    public AnalysisOptions(IEnumerable<ChecksumAlgorithm> algorithms, bool inspectSignature,
        RevocationMode revocationMode = RevocationMode.OfflineCache,
        bool inspectFileProperties = false, bool inspectPeHeader = false)
    {
        ArgumentNullException.ThrowIfNull(algorithms);
        var selected = algorithms.Distinct().ToArray();
        if (selected.Any(a => !Enum.IsDefined(a)))
            throw new ArgumentOutOfRangeException(nameof(algorithms));
        if (!Enum.IsDefined(revocationMode))
            throw new ArgumentOutOfRangeException(nameof(revocationMode));
        Algorithms = Array.AsReadOnly(selected);
        InspectSignature = inspectSignature;
        RevocationMode = revocationMode;
        InspectFileProperties = inspectFileProperties;
        InspectPeHeader = inspectPeHeader;
    }
}

public sealed record CertificateInfo(
    string Publisher, string Subject, string Issuer, string SerialNumber,
    string ThumbprintSha1, string ThumbprintSha256,
    DateTimeOffset NotBefore, DateTimeOffset NotAfter, string SignatureAlgorithm);

public enum TimestampStatus { NotPresent, Unverified, Verified, Unreadable }

public sealed record SignatureTimestamp(DateTimeOffset? Time, TimestampStatus Status)
{
    public static SignatureTimestamp NotPresent { get; } = new(null, TimestampStatus.NotPresent);
    public static SignatureTimestamp Unreadable { get; } = new(null, TimestampStatus.Unreadable);
}

// One file signature, not one certificate in its trust chain. Index is the
// Windows signature index (zero-based); timestamps belong to this signer only.
public sealed record EmbeddedSignature(
    uint Index, SignatureStatus Status, CertificateInfo? Certificate,
    int? NativeStatusCode, string? Diagnostic, string DigestAlgorithm,
    SignatureTimestamp Timestamp);

public sealed record SignatureResult(
    SignatureStatus Status, RevocationMode RevocationMode,
    CertificateInfo? Certificate = null, int? NativeStatusCode = null,
    string? Diagnostic = null)
{
    // The original properties describe the primary signature for compatibility.
    // Each additional signature has its own independent verification result.
    public IReadOnlyList<EmbeddedSignature> Signatures { get; init; } = [];
    public static SignatureResult NotChecked { get; } =
        new(SignatureStatus.NotChecked, RevocationMode.OfflineCache);
}

public sealed record FileAnalysisResult(
    string FullPath, long? Length, DateTimeOffset? LastWriteTime,
    IReadOnlyDictionary<ChecksumAlgorithm, string> Checksums,
    SignatureResult Signature, AnalysisError Error = AnalysisError.None,
    string? Diagnostic = null)
{
    public FilePropertiesResult? Properties { get; init; }
    public PeHeaderResult? PeHeader { get; init; }
    public bool Succeeded => Error == AnalysisError.None;

    public static FileAnalysisResult Failure(string path, AnalysisError error, string? diagnostic) =>
        new(path, null, null, new ReadOnlyDictionary<ChecksumAlgorithm, string>(
            new Dictionary<ChecksumAlgorithm, string>()), SignatureResult.NotChecked, error, diagnostic);
}

public sealed record AnalysisProgress(AnalysisStage Stage, long BytesRead, long TotalBytes);
