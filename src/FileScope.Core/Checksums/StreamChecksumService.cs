using System.Buffers;
using System.Collections.ObjectModel;
using System.Diagnostics;
using FileScope.Core.Abstractions;
using FileScope.Core.Models;

namespace FileScope.Core.Checksums;

public sealed class StreamChecksumService(IChecksumStrategyFactory factory) : IChecksumService
{
    public async Task<IReadOnlyDictionary<ChecksumAlgorithm, string>> ComputeAsync(
        Stream stream, IReadOnlyCollection<ChecksumAlgorithm> algorithms,
        IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(algorithms);
        cancellationToken.ThrowIfCancellationRequested();
        var strategies = new List<IChecksumStrategy>();
        byte[]? buffer = null;
        try
        {
            foreach (var algorithm in algorithms.Distinct()) strategies.Add(factory.Create(algorithm));
            var results = new Dictionary<ChecksumAlgorithm, string>();
            // Metadata-only and signature-only analyses need no checksum read.
            if (strategies.Count == 0) return new ReadOnlyDictionary<ChecksumAlgorithm, string>(results);
            buffer = ArrayPool<byte>.Shared.Rent(128 * 1024);
            long bytesRead = 0;
            var clock = Stopwatch.StartNew();
            int count;
            while ((count = await stream.ReadAsync(buffer.AsMemory(0, 128 * 1024), cancellationToken)
                       .ConfigureAwait(false)) != 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var strategy in strategies) strategy.Append(buffer.AsSpan(0, count));
                bytesRead += count;
                if (clock.ElapsedMilliseconds >= 100)
                {
                    progress?.Report(bytesRead);
                    clock.Restart();
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var strategy in strategies) results.Add(strategy.Algorithm, strategy.Complete());
            progress?.Report(bytesRead);
            return new ReadOnlyDictionary<ChecksumAlgorithm, string>(results);
        }
        finally
        {
            foreach (var strategy in strategies) strategy.Dispose();
            if (buffer is not null) ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
