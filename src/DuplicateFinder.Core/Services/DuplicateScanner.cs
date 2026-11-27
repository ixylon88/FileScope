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
        var catalogPaths = options.Catalogs && OperatingSystem.IsWindows() ? new Dictionary<FileKey, string>() : null;
        var files = Discover(roots, options.SizeRange, catalogPaths, issues, progress, token);
        var sizes = files.Where(f => f.Size.HasValue).CountBy(f => f.Size!.Value).ToDictionary();
        var algorithms = options.Hashes.Append(ChecksumAlgorithm.SHA256).Distinct().ToArray();

        for (var index = 0; index < files.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            var file = files[index];
            if (file.Stamp == null || sizes[file.Stamp.Length] == 1) continue;

            try
            {
                progress?.Report(new("Hash", file.Path, index, files.Count));
                using var input = FileAccessChecks.OpenVerified(file, _facts);
                file.Hashes = await _checksums.ComputeAsync(input, algorithms, cancellationToken: token).ConfigureAwait(false);
            }
            catch (Exception ex) when (FileAccessChecks.Expected(ex))
            {
                file.Diagnostic = ex.Message;
            }
        }

        WindowsCatalogIndex? catalogIndex = null;
        Classify(files, InspectSignatures, progress, token);
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

                if (catalogPaths != null && OperatingSystem.IsWindows())
                {
                    catalogIndex ??= new WindowsCatalogIndex(CatalogsToIndex(catalogPaths, progress),
                        (path, reason) => issues.Add(new(path, reason)), token);
                    progress?.Report(new("Catalog", file.Path, 0, 0));
                    file.Catalogs = catalogIndex.Inspect(input, options.Revocation, token);
                }
            }
            catch (Exception ex) when (FileAccessChecks.Expected(ex))
            {
                file.Diagnostic = ex.Message;
            }
        }
    }

    private List<FoundFile> Discover(IReadOnlyList<SearchRoot> roots, FileSizeRange sizes,
        Dictionary<FileKey, string>? catalogPaths, List<ScanIssue> issues,
        IProgress<ScanProgress>? progress, CancellationToken token)
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

            if (sizes == default || (catalogPaths != null && file.FullName.EndsWith(".cat", StringComparison.OrdinalIgnoreCase)))
                return true;

            try
            {
                // Directory enumeration supplies Length on Windows. Reparse points still take the diagnostic path.
                return (file.Attributes & FileAttributes.ReparsePoint) != 0 || sizes.Includes(file.Length);
            }
            catch (Exception ex) when (FileAccessChecks.Expected(ex))
            {
                // Resolve unknown metadata through the usual handle check and its per-file error reporting.
                return true;
            }
        }

        foreach (var selected in roots.OrderByDescending(r => r.Priority))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var root = selected with { Path = Path.GetFullPath(selected.Path) };
                ReportSearch(root.Path);
                FileAccessChecks.CheckPath(root.Path);
                var found = scanner.Scan(root.Path, IncludeFile, ReportSearch, token);
                ReportSearch(root.Path);
                issues.AddRange(found.Issues.Select(i => new ScanIssue(i.Path, i.Message)));

                for (var index = 0; index < found.Files.Count; index++)
                {
                    token.ThrowIfCancellationRequested();
                    var path = found.Files[index];
                    if (!paths.Add(path)) continue;

                    progress?.Report(new("Metadata", path, index, found.Files.Count));
                    var entry = new FoundFile(path, root);
                    try
                    {
                        FileAccessChecks.CheckPath(path);
                        using var input = FileAccessChecks.Open(path);
                        entry.Stamp = _facts.Read(input);

                        // A catalog's own size is independent of the files it describes.
                        if (catalogPaths != null && path.EndsWith(".cat", StringComparison.OrdinalIgnoreCase))
                        {
                            catalogPaths.TryAdd(entry.Stamp.Key, path);
                        }

                        // A candidate can change between directory enumeration and opening.
                        if (!sizes.Includes(entry.Stamp.Length)) continue;
                        if (!identities.Add(entry.Stamp.Key)) continue;
                    }
                    catch (Exception ex) when (FileAccessChecks.Expected(ex))
                    {
                        if (sizes != default)
                        {
                            // A file of unknown size cannot qualify for an active range.
                            issues.Add(new(path, ex.Message));
                            continue;
                        }
                        entry.Diagnostic = ex.Message;
                    }

                    files.Add(entry);
                }
            }
            catch (Exception ex) when (FileAccessChecks.Expected(ex)) { issues.Add(new(selected.Path, ex.Message)); }
        }

        return files;
    }

    private static IEnumerable<string> CatalogsToIndex(Dictionary<FileKey, string> catalogs, IProgress<ScanProgress>? progress)
    {
        var index = 0;
        foreach (var path in catalogs.Values)
        {
            progress?.Report(new("CatalogIndex", path, index, catalogs.Count));
            index++;
            yield return path;
        }
    }

    private void Classify(List<FoundFile> files, Action<FoundFile, FileStream> inspectSignatures,
        IProgress<ScanProgress>? progress, CancellationToken token)
    {
        var nextGroup = 0;
        var nextVariant = 0;
        var buckets = files.Where(f => f.Diagnostic == null && f.Hashes.ContainsKey(ChecksumAlgorithm.SHA256))
            .GroupBy(f => (f.Size, f.Hashes[ChecksumAlgorithm.SHA256]));
        foreach (var bucket in buckets)
        {
            token.ThrowIfCancellationRequested();
            if (bucket.Count() < 2) continue;

            // A hash collision can yield several actual groups. Compare against a
            // representative of EACH class rather than dropping nonmatching peers.
            var mainClasses = Partition(bucket, (a, b) =>
            {
                using var left = FileAccessChecks.OpenVerified(a, _facts);
                using var right = FileAccessChecks.OpenVerified(b, _facts);
                return BinaryComparison.Equal(left, right, token);
            }, progress, token);

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

                var variants = Partition(group.Where(f => f.Manifest != null), (a, b) =>
                {
                    using var left = FileAccessChecks.OpenVerified(a, _facts);
                    using var right = FileAccessChecks.OpenVerified(b, _facts);
                    return BinaryComparison.EqualNamed(a, b, token);
                }, progress, token);
                foreach (var peers in variants)
                {
                    var variant = ++nextVariant;
                    foreach (var file in peers)
                    {
                        file.Variant = variant;
                        file.Streams = variants.Count > 1 ? StreamComparison.Different :
                            peers.Count == group.Count ? StreamComparison.Identical : StreamComparison.Unknown;
                    }
                }
            }
        }
    }

    private static List<List<FoundFile>> Partition(IEnumerable<FoundFile> candidates,
        Func<FoundFile, FoundFile, bool> equal, IProgress<ScanProgress>? progress, CancellationToken token)
    {
        var classes = new List<List<FoundFile>>();
        foreach (var file in candidates)
        {
            token.ThrowIfCancellationRequested();
            progress?.Report(new("Compare", file.Path, 0, 0));
            try
            {
                var peers = classes.Find(group => equal(group[0], file));
                if (peers == null) classes.Add([file]);
                else peers.Add(file);
            }
            catch (Exception ex) when (FileAccessChecks.Expected(ex)) { file.Diagnostic = ex.Message; }
        }

        return classes;
    }
}
