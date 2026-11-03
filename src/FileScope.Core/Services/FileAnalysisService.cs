using System.Security;
using System.Security.Cryptography;
using FileScope.Core.Abstractions;
using FileScope.Core.Models;

namespace FileScope.Core.Services;

public sealed class FileAnalysisService(IChecksumService checksums, ISignatureInspector signatures)
    : IFileAnalysisService
{
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
            await using var stream = new FileStream(fullPath, new FileStreamOptions
            {
                Mode = FileMode.Open, Access = FileAccess.Read, Share = FileShare.Read,
                // Keep a synchronous native handle for Windows SIP providers.
                // ReadAsync still supplies the asynchronous managed read API;
                // the UI invokes this service on a background worker.
                BufferSize = 128 * 1024, Options = FileOptions.SequentialScan
            });
            var length = stream.Length;
            var modified = new DateTimeOffset(File.GetLastWriteTimeUtc(stream.SafeFileHandle));
            progress?.Report(new AnalysisProgress(AnalysisStage.Reading, 0, length));
            var hashProgress = new ForwardProgress<long>(n =>
                progress?.Report(new AnalysisProgress(AnalysisStage.Reading, n, length)));
            var hashes = await checksums.ComputeAsync(stream, options.Algorithms, hashProgress, cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var signature = SignatureResult.NotChecked;
            if (options.InspectSignature)
            {
                progress?.Report(new AnalysisProgress(AnalysisStage.CheckingSignature, length, length));
                stream.Position = 0;
                signature = signatures.Inspect(stream, fullPath, options.RevocationMode, cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            return new FileAnalysisResult(fullPath, length, modified, hashes, signature);
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

    private sealed class ForwardProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
