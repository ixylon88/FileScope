using DuplicateFinder.Core.Abstractions;
using DuplicateFinder.Core.Files;
using DuplicateFinder.Core.Models;

namespace DuplicateFinder.Core.Services;

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
        using var keeper = OpenKeeper(item, out var keeperFile, token);
        List<FileStream>? heldStreams = null;
        try
        {
            if (keeper != null && keeperFile != null)
            {
                if (candidate.IsHardLink || candidate.Root.Priority >= keeperFile.Root.Priority
                    || candidate.Variant <= 0 || candidate.Variant != keeperFile.Variant
                    || candidate.Stamp!.Key == keeperFile.Stamp!.Key || candidate.Manifest == null
                    || !candidate.Manifest.SequenceEqual(_facts.Streams(source))
                    || !candidate.Manifest.SequenceEqual(_facts.Streams(keeper))
                    || !BinaryComparison.Equal(new(candidate, source), new(keeperFile, keeper), token))
                {
                    throw new IOException("The verified copy has changed.");
                }

                // Sharing modes apply per stream. Deny writers on both files and
                // hold the keeper against deletion for the entire shell operation.
                foreach (var stream in candidate.Manifest)
                {
                    var a = FileAccessChecks.Open(candidate.Path + stream.Suffix, movable: true);
                    (heldStreams ??= []).Add(a);
                    var b = FileAccessChecks.Open(keeperFile.Path + stream.Suffix);
                    heldStreams.Add(b);
                    if (!BinaryComparison.Equal(new(candidate, a), new(keeperFile, b), token))
                        throw new IOException("A named stream has changed.");
                }
            }

            void Validate()
            {
                token.ThrowIfCancellationRequested();
                // The shell resolves a path. Check that it still names the same
                // scanned file, and that neither side acquired a new stream.
                using var current = FileAccessChecks.OpenVerified(candidate, _facts, movable: true);
                if (keeper == null || keeperFile == null) return;
                using var reference = FileAccessChecks.OpenVerified(keeperFile, _facts);
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
            if (heldStreams != null)
            {
                foreach (var stream in heldStreams) stream.Dispose();
            }
        }
    }

    private FileStream? OpenKeeper(CleanupItem item, out FoundFile? keeper, CancellationToken token)
    {
        keeper = item.Keeper;
        if (keeper is null) return null; // Explicit manual selection.

        // Select the reference before comparing or invoking the shell. A failed handoff must never be retried.
        foreach (var alternate in item.AlternateKeepers.Span)
        {
            token.ThrowIfCancellationRequested();
            try { return FileAccessChecks.OpenVerified(keeper, _facts); }
            catch (Exception ex) when (FileAccessChecks.Expected(ex)) { keeper = alternate; }
        }

        token.ThrowIfCancellationRequested();
        return FileAccessChecks.OpenVerified(keeper, _facts);
    }
}
