using System.Diagnostics;
using DuplicateFinder.Core.Abstractions;
using DuplicateFinder.Core.Files;
using DuplicateFinder.Core.Models;
using FileScope.Core.Abstractions;
using FileScope.Core.Checksums;
using FileScope.Core.Files;
using FileScope.Core.Models;
using FileScope.Core.Signatures;

namespace DuplicateFinder.Core.Services;

public sealed class DuplicateScanner
{
    private readonly IFileFacts _facts;
    private readonly IChecksumService _checksums;
    private readonly ISignatureInspector _signatures;

    public DuplicateScanner() : this(new WindowsFileFacts(),
        new StreamChecksumService(new ChecksumStrategyFactory()), new WindowsAuthenticodeInspector())
    { }

    internal DuplicateScanner(IFileFacts facts, IChecksumService checksums, ISignatureInspector signatures)
    {
        _facts = facts;
        _checksums = checksums;
        _signatures = signatures;
    }

    /// <summary>Run on a worker. Publishes a complete snapshot only on successful completion.</summary>
    public async Task<ScanResult> ScanAsync(IReadOnlyList<SearchRoot> roots, SearchOptions options,
        IProgress<ScanProgress>? progress = null, CancellationToken token = default)
    {
        var issues = new List<ScanIssue>();
        var catalogs = options.Catalogs && OperatingSystem.IsWindows() ? new ScanCatalogs(_facts, issues, progress) : null;
        var files = Discover(roots, options.SizeRange, catalogs, issues, progress, token);
        var candidates = FilesWithPeers(files, file => file.Size, token);

        if (candidates.Length > 0)
        {
            await HashCandidatesAsync(candidates, options.Hashes, progress, token).ConfigureAwait(false);
            var matches = FilesWithPeers(candidates, ContentKey, token);
            if (matches.Length > 0) Classify(matches, InspectSignatures, progress, token);
        }

        token.ThrowIfCancellationRequested();
        return new(files.AsReadOnly(), issues.AsReadOnly());

        // Called only for members of a byte-confirmed group, on the handle used for stream metadata.
        void InspectSignatures(FoundFile file, FileStream input)
        {
            try
            {
                if (options.Embedded)
                {
                    progress?.Report(new("Signature", file.Path, 0, 0));
                    file.Embedded = _signatures.Inspect(input, file.Path, options.Revocation, token);
                }

                if (catalogs != null && OperatingSystem.IsWindows())
                    file.Catalogs = catalogs.Inspect(input, options.Revocation, token);
            }
            catch (Exception ex) when (FileAccessChecks.Expected(ex))
            {
                file.Diagnostic = ex.Message;
            }
        }
    }

    private List<FoundFile> Discover(IReadOnlyList<SearchRoot> roots, FileSizeRange sizes, ScanCatalogs? catalogs,
        List<ScanIssue> issues, IProgress<ScanProgress>? progress, CancellationToken token)
    {
        // Highest priority first, including roots contained in another selected root.
        var paths = new HashSet<string>(StringComparer.Ordinal);
        var identities = new HashSet<FileKey>();
        var files = new List<FoundFile>();
        var scanner = new FolderScanner(FileAccessChecks.IsRecycleDirectory);
        var seen = 0;
        var updateClock = Stopwatch.StartNew();

        void ReportSearch(string path)
        {
            progress?.Report(new("Search", path, seen, 0));
            updateClock.Restart();
        }

        bool IncludeFile(FileInfo file)
        {
            seen++;
            if (progress != null && updateClock.ElapsedMilliseconds >= 150) ReportSearch(file.FullName);

            if (sizes == default) return true;

            try
            {
                // Directory enumeration supplies Length on Windows. Reparse points still take the diagnostic path.
                if ((file.Attributes & FileAttributes.ReparsePoint) != 0 || sizes.Includes(file.Length)) return true;

                // An out-of-range catalog may describe a duplicate. Its path is enough until one is confirmed.
                if (catalogs != null && CatalogSearch.IsCatalogPath(file.FullName)) catalogs.Remember(file.FullName);
                return false;
            }
            catch (Exception ex) when (FileAccessChecks.Expected(ex))
            {
                // Resolve unknown metadata through the usual handle check and its per-file error reporting.
                return true;
            }
        }

        var orderedRoots = OrderRoots(roots, issues, token);
        HashSet<string>? completedDirectories = HasOverlappingRoots(orderedRoots)
            ? new(OperatingSystem.IsWindows() ? new DirectoryPathComparer(WindowsFileFacts.TryReadDirectoryKey) : StringComparer.Ordinal) : null;
        foreach (var root in orderedRoots)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                ReportSearch(root.Path);
                FileAccessChecks.CheckPath(root.Path);
                if (completedDirectories?.Contains(root.Path) == true) continue;

                var found = scanner.Scan(root.Path, IncludeFile, ReportSearch, completedDirectories, token);
                ReportSearch(root.Path);
                issues.AddRange(found.Issues.Select(i => new ScanIssue(i.Path, i.Message)));

                for (var index = 0; index < found.Files.Count; index++)
                {
                    token.ThrowIfCancellationRequested();
                    var path = found.Files[index];
                    if (!paths.Add(path)) continue;

                    progress?.Report(new("Metadata", path, index, found.Files.Count));
                    FileStamp stamp;
                    try
                    {
                        FileAccessChecks.CheckPath(path);
                        using var input = FileAccessChecks.Open(path);
                        stamp = _facts.Read(input);

                        // A catalog's own size is independent of the files it describes.
                        if (catalogs != null && CatalogSearch.IsCatalogPath(path)) catalogs.Remember(path, stamp.Key);

                        // A candidate can change between directory enumeration and opening.
                        if (!sizes.Includes(stamp.Length) || !identities.Add(stamp.Key)) continue;
                    }
                    catch (Exception ex) when (FileAccessChecks.Expected(ex))
                    {
                        if (sizes != default)
                        {
                            // A file of unknown size cannot qualify for an active range.
                            issues.Add(new(path, ex.Message));
                        }
                        else
                        {
                            files.Add(new FoundFile(path, root) { Diagnostic = ex.Message });
                        }
                        continue;
                    }

                    files.Add(new FoundFile(path, root) { Stamp = stamp });
                }
            }
            catch (Exception ex) when (FileAccessChecks.Expected(ex)) { issues.Add(new(root.Path, ex.Message)); }
        }

        return files;
    }

    private static List<SearchRoot> OrderRoots(IReadOnlyList<SearchRoot> roots, List<ScanIssue> issues, CancellationToken token)
    {
        var ordered = new List<SearchRoot>(roots.Count);
        foreach (var root in roots.OrderByDescending(root => root.Priority))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root.Path));
                ordered.Add(root with { Path = path });
            }
            catch (Exception ex) when (FileAccessChecks.Expected(ex)) { issues.Add(new(root.Path, ex.Message)); }
        }

        return ordered;
    }

    private static bool HasOverlappingRoots(List<SearchRoot> roots)
    {
        if (roots.Count < 2) return false;

        var selected = roots.Select(root => root.Path).ToHashSet(
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        if (selected.Count < roots.Count) return true;

        foreach (var root in roots)
        {
            for (var parent = Path.GetDirectoryName(root.Path); parent != null; parent = Path.GetDirectoryName(parent))
            {
                if (selected.Contains(parent)) return true;
            }
        }

        return false;
    }

    // Keep discovery order and allocate comparison groups only for files that actually have a peer.
    private static FoundFile[] FilesWithPeers<TKey>(IReadOnlyList<FoundFile> files, Func<FoundFile, TKey?> keyOf,
        CancellationToken token) where TKey : struct
    {
        if (files.Count < 2) return [];

        var seen = new HashSet<TKey>();
        HashSet<TKey>? repeated = null;
        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();
            if (keyOf(file) is { } key && !seen.Add(key))
            {
                (repeated ??= []).Add(key);
            }
        }

        if (repeated is null) return [];
        return files.Where(file => keyOf(file) is { } key && repeated.Contains(key)).ToArray();
    }

    private async Task HashCandidatesAsync(FoundFile[] candidates, IReadOnlyList<ChecksumAlgorithm> selected,
        IProgress<ScanProgress>? progress, CancellationToken token)
    {
        var algorithms = selected.Append(ChecksumAlgorithm.SHA256).Distinct().ToArray();
        for (var index = 0; index < candidates.Length; index++)
        {
            token.ThrowIfCancellationRequested();
            var file = candidates[index];
            try
            {
                progress?.Report(new("Hash", file.Path, index, candidates.Length));
                using var input = FileAccessChecks.OpenVerified(file, _facts);
                file.Hashes = await _checksums.ComputeAsync(input, algorithms, cancellationToken: token).ConfigureAwait(false);
            }
            catch (Exception ex) when (FileAccessChecks.Expected(ex)) { file.Diagnostic = ex.Message; }
        }
    }

    private static (long Size, string Hash)? ContentKey(FoundFile file) =>
        file.Diagnostic is null && file.Size is long size && file.Hashes.TryGetValue(ChecksumAlgorithm.SHA256, out var hash)
            ? (size, hash) : null;

    private void Classify(FoundFile[] matches, Action<FoundFile, FileStream> inspectSignatures,
        IProgress<ScanProgress>? progress, CancellationToken token)
    {
        var nextGroup = 0;
        var nextVariant = 0;
        foreach (var bucket in matches.GroupBy(ContentKey))
        {
            token.ThrowIfCancellationRequested();
            // A hash collision can yield several actual groups. Compare against a
            // representative of EACH class rather than dropping nonmatching peers.
            var mainClasses = Partition(bucket, namedStreams: false, progress, token);

            foreach (var group in mainClasses.Where(g => g.Count > 1))
            {
                var id = ++nextGroup;
                foreach (var file in group)
                {
                    token.ThrowIfCancellationRequested();
                    file.Group = id;
                    file.Streams = StreamComparison.Unknown;
                    try
                    {
                        using var input = FileAccessChecks.OpenVerified(file, _facts);
                        inspectSignatures(file, input);
                        file.Manifest = _facts.Streams(input);
                    }
                    catch (Exception ex) when (FileAccessChecks.Expected(ex)) { file.Diagnostic = ex.Message; }
                }

                var variants = Partition(group.Where(f => f.Manifest != null), namedStreams: true, progress, token);
                foreach (var peers in variants)
                {
                    var variant = ++nextVariant;
                    foreach (var file in peers)
                    {
                        file.Variant = variant;
                        // Two verified peers still match when another file's streams were unreadable.
                        file.Streams = variants.Count > 1 ? StreamComparison.Different :
                            peers.Count > 1 ? StreamComparison.Identical : StreamComparison.Unknown;
                    }
                }
            }
        }
    }

    private List<List<FoundFile>> Partition(IEnumerable<FoundFile> candidates, bool namedStreams,
        IProgress<ScanProgress>? progress, CancellationToken token)
    {
        var classes = new List<List<FoundFile>>();
        foreach (var file in candidates)
        {
            token.ThrowIfCancellationRequested();
            progress?.Report(new("Compare", file.Path, 0, 0));
            AddToClass(file);
        }

        return classes;

        void AddToClass(FoundFile candidate)
        {
            for (var index = 0; index < classes.Count;)
            {
                token.ThrowIfCancellationRequested();
                var peers = classes[index];
                try
                {
                    if (BinaryComparison.CompareFiles(peers[0], candidate, _facts, namedStreams, token))
                    {
                        peers.Add(candidate);
                        return;
                    }

                    index++;
                }
                catch (ComparisonReadException ex)
                {
                    ex.File.Diagnostic = ex.Message;
                    if (ReferenceEquals(ex.File, candidate)) return;

                    // Retry the same candidate against the next confirmed peer or the next class.
                    peers.RemoveAt(0);
                    if (peers.Count == 0) classes.RemoveAt(index);
                }
            }

            classes.Add([candidate]);
        }
    }
}
