using FileScope.Core.Abstractions;
using FileScope.Core.Checksums;
using FileScope.Core.Files;
using FileScope.Core.Models;
using FileScope.Core.Signatures;

namespace DuplicateFinder.Core;

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

        var catalogIndex = catalogPaths != null && files.Any(f => f.Stamp != null) && OperatingSystem.IsWindows()
            ? new WindowsCatalogIndex(catalogPaths.Values, (path, reason) => issues.Add(new(path, reason)), token) : null;

        var completed = 0;
        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();
            progress?.Report(new("Hash", file.Path, completed++, files.Count));
            if (file.Stamp == null) continue;

            try
            {
                using var input = FileAccessChecks.OpenVerified(file, _facts);
                if (sizes[file.Stamp.Length] > 1)
                {
                    file.Hashes = await _checksums.ComputeAsync(input, algorithms, cancellationToken: token).ConfigureAwait(false);
                }

                if (options.Embedded)
                {
                    progress?.Report(new("Signature", file.Path, completed - 1, files.Count));
                    input.Position = 0;
                    file.Embedded = _signatures.Inspect(input, file.Path, options.Revocation, token);
                }

                if (catalogIndex != null && OperatingSystem.IsWindows())
                {
                    progress?.Report(new("Catalog", file.Path, completed - 1, files.Count));
                    file.Catalogs = catalogIndex.Inspect(input, options.Revocation, token);
                }
            }
            catch (Exception ex) when (FileAccessChecks.Expected(ex))
            {
                file.Diagnostic = ex.Message;
            }
        }

        Classify(files, progress, token);
        token.ThrowIfCancellationRequested();
        return new(files.AsReadOnly(), issues.AsReadOnly());
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

        foreach (var selected in roots.OrderByDescending(r => r.Priority))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var root = selected with { Path = Path.GetFullPath(selected.Path) };
                progress?.Report(new("Search", root.Path, 0, 0));
                FileAccessChecks.CheckPath(root.Path);
                var found = scanner.Scan(root.Path, token);
                issues.AddRange(found.Issues.Select(i => new ScanIssue(i.Path, i.Message)));

                foreach (var path in found.Files)
                {
                    token.ThrowIfCancellationRequested();
                    if (!paths.Add(path)) continue;
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

    private void Classify(List<FoundFile> files, IProgress<ScanProgress>? progress, CancellationToken token)
    {
        var nextGroup = 0;
        var nextVariant = 0;
        var buckets = files.Where(f => f.Diagnostic == null && f.Hashes.ContainsKey(ChecksumAlgorithm.SHA256))
            .GroupBy(f => (f.Size, f.Hashes[ChecksumAlgorithm.SHA256]));
        foreach (var bucket in buckets)
        {
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
