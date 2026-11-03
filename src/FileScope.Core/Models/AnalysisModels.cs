using System.Collections.ObjectModel;

namespace FileScope.Core.Models;

public enum ChecksumAlgorithm { MD5, SHA1, SHA256, SHA384, SHA512 }
public enum RevocationMode { OfflineCache, Online }
public enum AnalysisStage { Reading, CheckingSignature }
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

    public AnalysisOptions(IEnumerable<ChecksumAlgorithm> algorithms, bool inspectSignature,
        RevocationMode revocationMode = RevocationMode.OfflineCache)
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
    }
}

public sealed record CertificateInfo(
    string Publisher, string Subject, string Issuer, string SerialNumber,
    string ThumbprintSha1, string ThumbprintSha256,
    DateTimeOffset NotBefore, DateTimeOffset NotAfter, string SignatureAlgorithm);

public sealed record SignatureResult(
    SignatureStatus Status, RevocationMode RevocationMode,
    CertificateInfo? Certificate = null, int? NativeStatusCode = null,
    string? Diagnostic = null)
{
    public static SignatureResult NotChecked { get; } =
        new(SignatureStatus.NotChecked, RevocationMode.OfflineCache);
}

public sealed record FileAnalysisResult(
    string FullPath, long? Length, DateTimeOffset? LastWriteTime,
    IReadOnlyDictionary<ChecksumAlgorithm, string> Checksums,
    SignatureResult Signature, AnalysisError Error = AnalysisError.None,
    string? Diagnostic = null)
{
    public bool Succeeded => Error == AnalysisError.None;

    public static FileAnalysisResult Failure(string path, AnalysisError error, string? diagnostic) =>
        new(path, null, null, new ReadOnlyDictionary<ChecksumAlgorithm, string>(
            new Dictionary<ChecksumAlgorithm, string>()), SignatureResult.NotChecked, error, diagnostic);
}

public sealed record AnalysisProgress(AnalysisStage Stage, long BytesRead, long TotalBytes);
