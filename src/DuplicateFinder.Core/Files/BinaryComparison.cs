using System.Buffers;

namespace DuplicateFinder.Core;

internal static class BinaryComparison
{
    internal static bool Equal(Stream left, Stream right, CancellationToken token)
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
                var a = left.ReadAtLeast(first.AsSpan(0, block), block, throwOnEndOfStream: false);
                var b = right.ReadAtLeast(second.AsSpan(0, block), block, throwOnEndOfStream: false);
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

    internal static bool EqualNamed(FoundFile a, FoundFile b, CancellationToken token)
    {
        if (a.Manifest == null || b.Manifest == null || !a.Manifest.SequenceEqual(b.Manifest)) return false;
        foreach (var stream in a.Manifest)
        {
            using var left = FileAccessChecks.Open(a.Path + stream.Suffix);
            using var right = FileAccessChecks.Open(b.Path + stream.Suffix);
            if (!Equal(left, right, token)) return false;
        }

        return true;
    }
}
