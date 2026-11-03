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

public interface ISignatureInspector
{
    // Called on a worker thread. The caller keeps the read lock/handle alive.
    SignatureResult Inspect(FileStream file, string fullPath, RevocationMode revocationMode,
        CancellationToken cancellationToken = default);
}
