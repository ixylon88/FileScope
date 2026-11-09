using System.ComponentModel;
using System.Security;
using System.Security.Cryptography;
using FileScope.Core.Abstractions;
using FileScope.Core.Models;
using FileScope.Core.Metadata;

namespace FileScope.Core.Services;

/// <summary>Owns one file stream and lends it to the checksum, metadata and signature strategies.</summary>
/// <remarks>
/// The strategies run sequentially so managed reads and native file-pointer changes cannot race.
/// Only checksum algorithms share one content pass; native inspection can perform additional reads.
/// </remarks>
public sealed class FileAnalysisService(IChecksumService checksums, ISignatureInspector signatures,
    IFilePropertiesInspector properties, IPeHeaderInspector peHeaders)
    : IFileAnalysisService
{
    public FileAnalysisService(IChecksumService checksums, ISignatureInspector signatures)
        : this(checksums, signatures, new FilePropertiesInspector(), new PeHeaderInspector()) { }

    public async Task<FileAnalysisResult> AnalyzeAsync(string path, AnalysisOptions options,
        IProgress<AnalysisProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();
        string fullPath = path;
        try
        {
            fullPath = Path.GetFullPath(path);
            if (Directory.Exists(fullPath))
                return FileAnalysisResult.Failure(fullPath, AnalysisError.DirectoryNotSupported, null);

            // FileShare.Read prevents writers/deletion on Windows for the entire analysis.
            // WinVerifyTrust receives this very same handle, not a newly opened file.
            progress?.Report(new AnalysisProgress(AnalysisStage.OpeningFile, 0, 0));
            await using var stream = new FileStream(fullPath, new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.Read,
                // Keep a synchronous native handle for Windows SIP providers.
                // ReadAsync still supplies the asynchronous managed read API;
                // the UI invokes this service on a background worker.
                BufferSize = 128 * 1024,
                Options = FileOptions.SequentialScan
            });
            var length = stream.Length;
            var modified = new DateTimeOffset(File.GetLastWriteTimeUtc(stream.SafeFileHandle));
            if (options.Algorithms.Count > 0) progress?.Report(new AnalysisProgress(AnalysisStage.Reading, 0, length));
            var hashProgress = new ForwardProgress<long>(n =>
                progress?.Report(new AnalysisProgress(AnalysisStage.Reading, n, length)));
            hashProgress.Report(0);
            var hashes = await checksums.ComputeAsync(stream, options.Algorithms, hashProgress, cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            FilePropertiesResult? fileProperties = null;
            PeHeaderResult? peHeader = null;
            if (options.InspectFileProperties)
            {
                progress?.Report(new AnalysisProgress(AnalysisStage.ReadingMetadata, length, length));
                fileProperties = ReadOptional(() => properties.Inspect(stream, fullPath, cancellationToken),
                    message => new FilePropertiesResult(null, message));
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (options.InspectPeHeader)
            {
                progress?.Report(new AnalysisProgress(AnalysisStage.ReadingPeHeader, length, length));
                // Hashing ends at EOF. Header inspection and Windows trust verification
                // each receive a defined starting position on the same borrowed stream.
                stream.Position = 0;
                peHeader = ReadOptional(() => peHeaders.Inspect(stream, cancellationToken),
                    message => new PeHeaderResult(null, message));
            }
            cancellationToken.ThrowIfCancellationRequested();
            var signature = SignatureResult.NotChecked;
            if (options.InspectSignature)
            {
                progress?.Report(new AnalysisProgress(AnalysisStage.CheckingSignature, length, length));
                stream.Position = 0;
                signature = signatures.Inspect(stream, fullPath, options.RevocationMode, cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            return new FileAnalysisResult(fullPath, length, modified, hashes, signature)
            { Properties = fileProperties, PeHeader = peHeader };
        }
        catch (OperationCanceledException) { throw; }
        catch (FileNotFoundException ex) { return Failure(AnalysisError.NotFound, ex); }
        catch (DirectoryNotFoundException ex) { return Failure(AnalysisError.NotFound, ex); }
        catch (UnauthorizedAccessException ex) { return Failure(AnalysisError.AccessDenied, ex); }
        catch (SecurityException ex) { return Failure(AnalysisError.AccessDenied, ex); }
        catch (IOException ex) { return Failure(AnalysisError.ReadFailure, ex); }
        catch (ArgumentException ex) { return Failure(AnalysisError.InvalidPath, ex); }
        catch (NotSupportedException ex) { return Failure(AnalysisError.InvalidPath, ex); }
        catch (CryptographicException ex) { return Failure(AnalysisError.AnalysisFailure, ex); }

        FileAnalysisResult Failure(AnalysisError error, Exception ex) =>
            FileAnalysisResult.Failure(fullPath, error, ex.Message);
    }

    // Optional metadata failures become diagnostics without discarding completed hashes.
    // Cancellation is deliberately outside this filter and reaches the caller unchanged.
    private static T ReadOptional<T>(Func<T> read, Func<string, T> failure)
    {
        try { return read(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException
            or Win32Exception or BadImageFormatException or ArgumentException or NotSupportedException)
        { return failure(ex.Message); }
    }

    private sealed class ForwardProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
