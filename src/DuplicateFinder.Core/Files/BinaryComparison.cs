using System.Buffers;
using DuplicateFinder.Core.Abstractions;
using DuplicateFinder.Core.Models;

namespace DuplicateFinder.Core.Files;

internal static class BinaryComparison
{
    internal static bool CompareFiles(FoundFile reference, FoundFile candidate, IFileFacts facts,
        bool namedStreams, CancellationToken token)
    {
        using var left = Open(reference, facts);
        using var right = Open(candidate, facts);
        if (!namedStreams) return Equal(new(reference, left), new(candidate, right), token);

        if (reference.Manifest == null || candidate.Manifest == null || !reference.Manifest.SequenceEqual(candidate.Manifest))
            return false;

        foreach (var stream in reference.Manifest)
        {
            using var first = Open(reference, facts, stream.Suffix);
            using var second = Open(candidate, facts, stream.Suffix);
            if (!Equal(new(reference, first), new(candidate, second), token)) return false;
        }

        return true;
    }

    private static FileStream Open(FoundFile file, IFileFacts facts, string? suffix = null)
    {
        try
        {
            return suffix is null ? FileAccessChecks.OpenVerified(file, facts) : FileAccessChecks.Open(file.Path + suffix);
        }
        catch (Exception ex) when (FileAccessChecks.Expected(ex)) { throw new ComparisonReadException(file, ex); }
    }

    internal static bool Equal(Input left, Input right, CancellationToken token)
    {
        if (left.Length != right.Length) return false;
        const int block = 128 * 1024;
        var first = ArrayPool<byte>.Shared.Rent(block);
        var second = ArrayPool<byte>.Shared.Rent(block);
        try
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var a = left.ReadBlock(first.AsSpan(0, block));
                var b = right.ReadBlock(second.AsSpan(0, block));
                if (a != b || !first.AsSpan(0, a).SequenceEqual(second.AsSpan(0, b))) return false;
                if (a < block) return true;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(second);
            ArrayPool<byte>.Shared.Return(first);
        }
    }

    // Keep the owning row with every read, including reads from a named stream.
    internal readonly struct Input(FoundFile file, Stream stream)
    {
        internal long Length
        {
            get
            {
                try { return stream.Length; }
                catch (Exception ex) when (FileAccessChecks.Expected(ex)) { throw new ComparisonReadException(file, ex); }
            }
        }

        internal int ReadBlock(Span<byte> block)
        {
            try { return stream.ReadAtLeast(block, block.Length, throwOnEndOfStream: false); }
            catch (Exception ex) when (FileAccessChecks.Expected(ex)) { throw new ComparisonReadException(file, ex); }
        }
    }
}

internal sealed class ComparisonReadException(FoundFile file, Exception cause) : IOException(cause.Message, cause)
{
    internal FoundFile File { get; } = file;
}
