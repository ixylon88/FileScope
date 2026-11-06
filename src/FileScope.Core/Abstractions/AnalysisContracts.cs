using FileScope.Core.Models;

namespace FileScope.Core.Abstractions;

public interface IFileAnalysisService
{
    Task<FileAnalysisResult> AnalyzeAsync(string path, AnalysisOptions options,
        IProgress<AnalysisProgress>? progress = null, CancellationToken cancellationToken = default);
}

public interface IChecksumStrategy : IDisposable
{
    ChecksumAlgorithm Algorithm { get; }
    void Append(ReadOnlySpan<byte> data);
    string Complete();
}

public interface IChecksumStrategyFactory
{
    IChecksumStrategy Create(ChecksumAlgorithm algorithm);
}

public interface IChecksumService
{
    Task<IReadOnlyDictionary<ChecksumAlgorithm, string>> ComputeAsync(
        Stream stream, IReadOnlyCollection<ChecksumAlgorithm> algorithms,
        IProgress<long>? progress = null, CancellationToken cancellationToken = default);
}

/// <summary>Inspects signatures on a file whose stream remains owned by the caller.</summary>
/// <remarks>
/// The caller must not close or concurrently use the stream during inspection.
/// Native implementations may move the file pointer and observe cancellation only between API calls.
/// </remarks>
public interface ISignatureInspector
{
    // Called on a worker thread. The caller keeps the read lock/handle alive.
    SignatureResult Inspect(FileStream file, string fullPath, RevocationMode revocationMode,
        CancellationToken cancellationToken = default);
}

/// <summary>Reads optional file properties without taking ownership of the supplied stream.</summary>
/// <remarks>
/// Windows handle access synchronizes the native pointer with the managed position before inspection.
/// The Windows implementation restores this synchronized position after the native version query.
/// </remarks>
public interface IFilePropertiesInspector
{
    // Borrows the original file, leaves it open and preserves its position.
    // The path supplies the extension and the compatibility fallback only.
    FilePropertiesResult Inspect(FileStream file, string fullPath, CancellationToken cancellationToken = default);
}

public interface IPeHeaderInspector
{
    // PE and NE header metadata. Reads bytes only, leaves the stream open and restores its position.
    PeHeaderResult Inspect(Stream stream, CancellationToken cancellationToken = default);
}
