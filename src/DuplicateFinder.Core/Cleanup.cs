namespace DuplicateFinder.Core;

public sealed record CleanupItem(FoundFile File, FoundFile? Keeper);
public sealed record CleanupPlan(IReadOnlyList<CleanupItem> Items)
{
    public int Groups => Items.Select(i => i.File.Group).Distinct().Count();

    public static CleanupPlan Create(IEnumerable<FoundFile> scan, Func<FoundFile, bool>? inScope = null)
    {
        var items = new List<CleanupItem>();
        foreach (var variant in scan.Where(f => f.Variant > 0).GroupBy(f => f.Variant))
        {
            var priority = variant.Max(f => f.Root.Priority);
            var keeper = variant.FirstOrDefault(f => f.Root.Priority == priority && f.Diagnostic == null
                && f.Streams != StreamComparison.Unknown);
            if (keeper == null) continue;

            foreach (var file in variant)
            {
                if (file.Root.Priority < priority && file.Diagnostic == null && !file.IsHardLink
                    && file.Streams != StreamComparison.Unknown && (inScope == null || inScope(file)))
                {
                    items.Add(new(file, keeper));
                }
            }
        }

        return new(items.OrderBy(i => i.File.Root.Priority).ThenBy(i => i.File.Path, StringComparer.Ordinal).ToArray());
    }
}

/// <summary>Invoke the guard immediately before handing this exact item to the shell for recycling.</summary>
public interface IRecycleBin
{
    void Recycle(string path, Action validate);
}

public sealed record RecycleResult(FoundFile File, bool Moved, string? Reason);

public sealed class CleanupService
{
    private readonly IRecycleBin _bin;
    private readonly IFileFacts _facts;

    public CleanupService(IRecycleBin bin) : this(bin, new WindowsFileFacts()) { }
    internal CleanupService(IRecycleBin bin, IFileFacts facts) { _bin = bin; _facts = facts; }

    public IReadOnlyList<RecycleResult> Execute(IEnumerable<CleanupItem> selection,
        IProgress<ScanProgress>? progress = null, CancellationToken token = default)
    {
        var results = new List<RecycleResult>();
        foreach (var item in selection)
        {
            if (token.IsCancellationRequested) break;
            try
            {
                progress?.Report(new("Recycle", item.File.Path, results.Count, 0));
                Recycle(item, token);
                results.Add(new(item.File, true, null));
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            // A shell provider can translate failures to several managed exception types.
            // Keep completed entries in the report and allow the remaining items to proceed.
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                results.Add(new(item.File, false, ex.Message));
            }
        }

        return results;
    }

    private void Recycle(CleanupItem item, CancellationToken token)
    {
        var candidate = item.File;
        using var source = FileAccessChecks.OpenVerified(candidate, _facts, movable: true);
        using var keeper = item.Keeper == null ? null : FileAccessChecks.OpenVerified(item.Keeper, _facts);
        var heldStreams = new List<FileStream>();
        try
        {
            if (keeper != null && item.Keeper != null)
            {
                if (candidate.IsHardLink || candidate.Root.Priority >= item.Keeper.Root.Priority
                    || candidate.Variant <= 0 || candidate.Variant != item.Keeper.Variant
                    || candidate.Stamp!.Key == item.Keeper.Stamp!.Key || candidate.Manifest == null
                    || !candidate.Manifest.SequenceEqual(_facts.Streams(source))
                    || !candidate.Manifest.SequenceEqual(_facts.Streams(keeper))
                    || !BinaryComparison.Equal(source, keeper, token))
                {
                    throw new IOException("The verified copy has changed.");
                }

                // Sharing modes apply per stream. Deny writers on both files and
                // hold the keeper against deletion for the entire shell operation.
                foreach (var stream in candidate.Manifest)
                {
                    var a = FileAccessChecks.Open(candidate.Path + stream.Suffix, movable: true);
                    heldStreams.Add(a);
                    var b = FileAccessChecks.Open(item.Keeper.Path + stream.Suffix);
                    heldStreams.Add(b);
                    if (!BinaryComparison.Equal(a, b, token)) throw new IOException("A named stream has changed.");
                }
            }

            void Validate()
            {
                token.ThrowIfCancellationRequested();
                // The shell resolves a path. Check that it still names the same
                // scanned file, and that neither side acquired a new stream.
                using var current = FileAccessChecks.OpenVerified(candidate, _facts, movable: true);
                if (keeper == null || item.Keeper == null) return;
                using var reference = FileAccessChecks.OpenVerified(item.Keeper, _facts);
                if (!candidate.Manifest!.SequenceEqual(_facts.Streams(current)) ||
                    !candidate.Manifest!.SequenceEqual(_facts.Streams(reference)))
                {
                    throw new IOException("The stream list has changed.");
                }
            }

            Validate();
            _bin.Recycle(candidate.Path, Validate);
        }
        finally
        {
            foreach (var stream in heldStreams) stream.Dispose();
        }
    }
}
