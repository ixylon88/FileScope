using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Text;
using DuplicateFinder.Core.Abstractions;
using DuplicateFinder.Core.Files;
using DuplicateFinder.Core.Models;
using DuplicateFinder.Core.Services;
using FileScope.Core.Abstractions;
using FileScope.Core.Checksums;
using FileScope.Core.Models;
using FileScope.Core.Signatures;
using FileScope.TestSupport;

namespace DuplicateFinder.Tests;

[TestClass]
public sealed partial class DuplicateTests
{
    private readonly string _dir = Directory.CreateTempSubdirectory("DuplicateFinder-tests-").FullName;
    private readonly Facts _facts = new();
    private static readonly SearchOptions Options = new([ChecksumAlgorithm.MD5, ChecksumAlgorithm.SHA512], false, false, RevocationMode.OfflineCache);

    [TestCleanup]
    public Task Cleanup() => TestDirectory.DeleteAsync(_dir);

    private string Put(string root, string name, string bytes)
    {
        var folder = Directory.CreateDirectory(Path.Combine(_dir, root)).FullName;
        var path = Path.Combine(folder, name);
        File.WriteAllText(path, bytes);
        return path;
    }

    private Task<ScanResult> ScanAsync(IChecksumService? service = null, SearchOptions? options = null,
        IProgress<ScanProgress>? progress = null, CancellationToken token = default)
    {
        var roots = Directory.GetDirectories(_dir).Select(d => new SearchRoot(d, int.Parse(Path.GetFileName(d),
            System.Globalization.CultureInfo.InvariantCulture))).ToArray();
        var scanner = new DuplicateScanner(_facts, service ?? new StreamChecksumService(new ChecksumStrategyFactory()),
            new WindowsAuthenticodeInspector());
        return scanner.ScanAsync(roots, options ?? Options, progress, token);
    }

    [TestMethod]
    public async Task DistinctSizesNeedOnlyDiscoveryEvenWithAllInspectionsEnabled()
    {
        var paths = new[] { Put("1", "empty", ""), Put("2", "one", "x"), Put("3", "single.cat", "catalog") };
        var progress = new Stages();
        var unused = new UnexpectedInspections();
        var scanner = new DuplicateScanner(_facts, unused, unused);
        var scan = await scanner.ScanAsync([new(_dir, 1)], Options with { Embedded = true, Catalogs = true }, progress);

        CollectionAssert.AreEquivalent(paths, scan.Files.Select(file => file.Path).ToArray());
        Assert.IsEmpty(scan.Issues);
        Assert.IsEmpty(_facts.StreamReads);
        Assert.IsTrue(progress.Updates.All(update => update.Stage is "Search" or "Metadata"));
        Assert.IsEmpty(CleanupPlan.Create(scan.Files).Items);

        foreach (var file in scan.Files)
        {
            Assert.AreEqual(1, _facts.ReadCount(file.Path));
            var name = file.Name;
            Assert.AreEqual(Path.GetFileName(file.Path), name);
            Assert.AreSame(name, file.Name);
            Assert.AreSame(ReadOnlyDictionary<ChecksumAlgorithm, string>.Empty, file.Hashes);
            Assert.AreEqual(SignatureResult.NotChecked, file.Embedded);
            Assert.IsNull(file.Catalogs);
            Assert.AreEqual(StreamComparison.Unmatched, file.Streams);
            Assert.IsNull(file.Diagnostic);
        }
    }

    [TestMethod]
    public async Task HashProgressOnlyReportsFilesWithAnotherCandidateOfTheSameSize()
    {
        var first = Put("1", "first", "ABC");
        var second = Put("2", "second", "XYZ");
        // Even a loose catalog (deliberately invalid here) is never indexed for an all-single scan.
        var single = Put("1", "single.cat", "longer");
        var progress = new Stages();
        var scanner = new DuplicateScanner(_facts, new StreamChecksumService(new ChecksumStrategyFactory()), new WindowsAuthenticodeInspector());
        var scan = await scanner.ScanAsync([new(_dir, 1)], Options with { Embedded = true, Catalogs = true }, progress);

        Assert.IsEmpty(scan.Issues);
        CollectionAssert.AreEquivalent(new[] { first, second }, progress.Paths("Hash"));
        Assert.IsTrue(progress.Updates.Where(p => p.Stage == "Hash").All(p => p.Total == 2));
        Assert.IsEmpty(progress.Paths("Compare"));
        Assert.IsEmpty(_facts.StreamReads);
        Assert.AreEqual(2, _facts.ReadCount(first));
        Assert.AreEqual(2, _facts.ReadCount(second));
        Assert.IsEmpty(progress.Paths("Signature"));
        Assert.IsEmpty(progress.Paths("Catalog"));
        Assert.IsEmpty(progress.Paths("CatalogIndex"));
        Assert.IsTrue(scan.Files.All(file => file.Embedded == SignatureResult.NotChecked && file.Catalogs == null));
        Assert.IsEmpty(scan.Files.Single(file => file.Path == single).Hashes);
        Assert.AreEqual(1, _facts.ReadCount(single), "A unique size needs only discovery even with both signature options enabled.");
        Assert.IsTrue(scan.Files.Where(file => file.Path != single).All(file => file.Hashes.Count == 3));
        Assert.IsTrue(scan.Files.All(file => file.Group == 0));
    }

    [TestMethod]
    public async Task SizeLimitsExcludeFilesBeforeAnalysisAndCleanup()
    {
        var admitted = new List<string>();
        var excluded = new List<string>();
        foreach (var size in Enumerable.Range(1, 5))
        {
            var contents = new string('x', size);
            var paths = size is >= 2 and <= 4 ? admitted : excluded;
            paths.Add(Put("9", "keep-" + contents, contents));
            paths.Add(Put("1", "copy-" + contents, contents));
        }

        // Directory metadata remains available while an unrelated file is held exclusively.
        using var locked = File.Open(excluded[0], FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var options = Options with { SizeRange = new(2, 4), Embedded = true };
        var scan = await ScanAsync(options: options);
        Assert.IsEmpty(scan.Issues);
        CollectionAssert.AreEquivalent(admitted, scan.Files.Select(f => f.Path).ToArray());
        Assert.IsTrue(scan.Files.All(f => f.Hashes.ContainsKey(ChecksumAlgorithm.SHA256) && f.Embedded != SignatureResult.NotChecked));
        Assert.IsTrue(excluded.All(path => _facts.ReadCount(path) == 0), "Out-of-range files should need no read handle.");

        var plan = CleanupPlan.Create(scan.Files);
        CollectionAssert.AreEquivalent(admitted.Where(path => Path.GetFileName(path).StartsWith("copy-", StringComparison.Ordinal)).ToArray(),
            plan.Items.Select(item => item.File.Path).ToArray());
    }

    [TestMethod]
    public async Task DiscoveryReportsSubfoldersAndRechecksCandidateSizeAtOpen()
    {
        var changed = Put("1/nested", "changed", "abc");
        var kept = Put("1/nested", "kept", "123");
        var excluded = Put("1/nested", "small", "x");
        var resized = false;

        var progress = new Stages(update =>
        {
            // The file qualified in the directory listing, but grows before its handle is opened.
            if (!resized && update.Stage == "Metadata" && update.Path == changed)
            {
                File.WriteAllText(changed, "larger");
                resized = true;
            }
        });

        var scanner = new DuplicateScanner(_facts, new StreamChecksumService(new ChecksumStrategyFactory()), new WindowsAuthenticodeInspector());
        var scan = await scanner.ScanAsync([new(_dir, 1)], Options with { SizeRange = new(3, 3) }, progress);

        Assert.IsTrue(resized);
        Assert.IsEmpty(scan.Issues);
        Assert.AreEqual(kept, scan.Files.Single().Path);
        Assert.AreEqual(1, _facts.ReadCount(changed));
        Assert.AreEqual(0, _facts.ReadCount(excluded));

        CollectionAssert.Contains(progress.Paths("Search"), Path.GetDirectoryName(changed)!);
        Assert.AreEqual(3, progress.Updates.Last(update => update.Stage == "Search").Completed);
        CollectionAssert.DoesNotContain(progress.Paths("Metadata"), excluded);
    }

    [TestMethod]
    public async Task EmptyFileAndOpenSizeLimitsWorkDuringDiscovery()
    {
        Put("1", "empty", "");
        Put("1", "one", "x");
        Put("1", "two", "xx");

        await Check(default, "empty", "one", "two");
        await Check(new(null, 0), "empty");
        await Check(new(1, null), "one", "two");
        await Check(new(1, 1), "one");
        await Check(new(3, null));

        async Task Check(FileSizeRange range, params string[] expected)
        {
            var scan = await ScanAsync(options: Options with { SizeRange = range });
            CollectionAssert.AreEquivalent(expected, scan.Files.Select(f => f.Name).ToArray());
        }
    }

    [TestMethod]
    public async Task UnknownSizeWithActiveRangeIsReportedOutsideResults()
    {
        var unknown = Put("1", "unknown", "file");
        _facts.UnreadableSize.Add(unknown);
        var unrestricted = await ScanAsync();
        Assert.IsNotNull(unrestricted.Files.Single().Diagnostic);

        var filtered = await ScanAsync(options: Options with { SizeRange = new(0, null) });
        Assert.IsEmpty(filtered.Files);
        Assert.AreEqual(unknown, filtered.Issues.Single().Path);
    }

    [TestMethod]
    public async Task AllHighestTiesSurviveEvenWhenFilteredOut()
    {
        Put("30", "master-a", "same");
        Put("30", "master-b", "same");
        var backup = Put("20", "backup", "same");
        var download = Put("10", "download", "same");
        var unique = Put("10", "only", "unique");
        var scan = await ScanAsync();

        var all = CleanupPlan.Create(scan.Files);
        var filtered = CleanupPlan.Create(scan.Files, f => f.Path == download);
        CollectionAssert.AreEqual(new[] { download, backup }, all.Items.Select(i => i.File.Path).ToArray());
        Assert.AreEqual(1, filtered.Groups);
        Assert.AreEqual(download, filtered.Items.Single().File.Path);
        Assert.AreEqual(30, filtered.Items.Single().Keeper!.Root.Priority);
        Assert.AreEqual(3, scan.Files.Single(f => f.Path == backup).Hashes.Count);
        Assert.IsEmpty(scan.Files.Single(f => f.Name == "only").Hashes);
        Assert.AreEqual(1, _facts.ReadCount(unique), "With no content checks, the discovery handle is sufficient.");
    }

    [TestMethod]
    public async Task PriorityIsLocalToEachGroupAndOverlappingRootsCountOnce()
    {
        var high = Put("40", "unrelated", "unique");
        var parent = Put("2", "a", "copy");
        var nested = Put("2/nested", "b", "copy");
        var scanner = new DuplicateScanner(_facts, new StreamChecksumService(new ChecksumStrategyFactory()), new WindowsAuthenticodeInspector());
        var scan = await scanner.ScanAsync([new(_dir, 1), new(Path.GetDirectoryName(parent)!, 2),
            new(Path.GetDirectoryName(nested)!, 5), new(Path.GetDirectoryName(high)!, 40)], Options);

        Assert.HasCount(3, scan.Files);
        var item = CleanupPlan.Create(scan.Files).Items.Single();
        Assert.AreEqual(parent, item.File.Path);
        Assert.AreEqual(nested, item.Keeper!.Path);
    }

    [TestMethod]
    public async Task HashCollisionsSplitIntoAllBinaryClasses()
    {
        Put("1", "a", "aaaa");
        Put("2", "b", "bbbb");
        Put("3", "c", "aaaa");
        Put("4", "d", "bbbb");
        Put("5", "e", "cccc");

        var scan = await ScanAsync(new Collision(), Options with { Embedded = true });
        Assert.IsTrue(scan.Files.Where(f => f.Group != 0).All(f => f.Embedded != SignatureResult.NotChecked));
        var single = scan.Files.Single(f => f.Name == "e");
        Assert.AreEqual(SignatureResult.NotChecked, single.Embedded);
        Assert.IsNull(single.Manifest);
        CollectionAssert.AreEquivalent(scan.Files.Where(f => f.Group != 0).Select(f => f.Path).ToArray(), _facts.StreamReads);
        var groups = scan.Files.ToDictionary(f => f.Name, f => f.Group);
        Assert.AreEqual(groups["a"], groups["c"]);
        Assert.AreEqual(groups["b"], groups["d"]);
        Assert.AreNotEqual(groups["a"], groups["b"]);
        Assert.AreEqual(0, groups["e"]);
        Assert.AreEqual(2, CleanupPlan.Create(scan.Files).Groups);
    }

    [TestMethod]
    public async Task AlternateStreamContentsAndOneSidedStreamsHaveIndependentKeepers()
    {
        var a = Put("30", "a", "same");
        var b = Put("20", "b", "same");
        var c = Put("10", "c", "same");
        var d = Put("5", "d", "same");
        WriteStream(a, "111");
        WriteStream(b, "222");
        WriteStream(c, "222");
        var scan = await ScanAsync();
        var named = scan.Files.Where(f => new[] { a, b, c, d }.Contains(f.Path)).ToArray();
        Assert.IsTrue(named.All(f => f.Streams == StreamComparison.Different));
        var plan = CleanupPlan.Create(named);
        Assert.AreEqual(c, plan.Items.Single().File.Path);
        Assert.AreEqual(b, plan.Items.Single().Keeper!.Path);
    }

    private void WriteStream(string path, string value)
    {
        const string suffix = ":example:$DATA";
        File.WriteAllText(path + suffix, value);
        _facts.Manifests[path] = [new(suffix, Encoding.UTF8.GetByteCount(value))];
    }

    [TestMethod]
    public async Task AnUnreadableStreamListIsNeverEligible()
    {
        var a = Put("10", "first", "same");
        var b = Put("1", "second", "same");
        _facts.Unreadable.Add(b);
        var scan = await ScanAsync();
        Assert.AreEqual(StreamComparison.Unknown, scan.Files.Single(f => f.Path == b).Streams);
        Assert.IsEmpty(CleanupPlan.Create(scan.Files).Items);
        Assert.IsTrue(File.Exists(a));
    }

    [TestMethod]
    public async Task HardLinksCountOnceAndAreNeverAutomaticCandidates()
    {
        var a = Put("1", "a", "copy");
        var b = Put("2", "b", "copy");
        Put("8", "reference", "copy");
        var same = new FileKey(7, Guid.NewGuid());
        _facts.Keys[a] = _facts.Keys[b] = same;
        _facts.Links[a] = _facts.Links[b] = 2;
        var scan = await ScanAsync();
        Assert.HasCount(2, scan.Files);
        Assert.IsTrue(scan.Files.Single(f => f.IsHardLink).Path == b);
        Assert.IsEmpty(CleanupPlan.Create(scan.Files).Items);
    }

    [TestMethod]
    public async Task RecycleDirectoryIsExcludedEvenWithReadableFiles()
    {
        Put("1", "base", "same");
        Put("1/$Recycle.Bin", "trashed", "same");
        var scan = await ScanAsync();
        Assert.AreEqual("base", scan.Files.Single().Name);
    }

    [TestMethod]
    public async Task ChangedCandidateAndDisappearedKeeperNeverReachTheBin()
    {
        var keeper = Put("9", "keep", "same");
        var candidate = Put("1", "low", "same");
        var plan = CleanupPlan.Create((await ScanAsync()).Files);
        var bin = new Bin();
        var cleanup = new CleanupService(bin, _facts);
        await TestDirectory.RetrySharingViolationAsync(() => File.WriteAllText(candidate, "different length"));
        Assert.IsFalse(cleanup.Execute(plan.Items).Single().Moved);

        await TestDirectory.RetrySharingViolationAsync(() => File.WriteAllText(candidate, "same"));
        var fresh = CleanupPlan.Create((await ScanAsync()).Files);
        await TestDirectory.RetrySharingViolationAsync(() => File.Move(keeper, keeper + ".moved"));
        Assert.IsFalse(cleanup.Execute(fresh.Items).Single().Moved);
        Assert.IsEmpty(bin.Paths);
        Assert.IsTrue(File.Exists(candidate));
    }

    [TestMethod]
    public async Task BinaryRecheckRejectsContentEvenWhenTheScanStampLooksIdentical()
    {
        var keeper = Put("9", "keep", "aaaa");
        var candidate = Put("1", "candidate", "aaaa");
        var plan = CleanupPlan.Create((await ScanAsync()).Files);
        Assert.AreEqual(candidate, plan.Items.Single().File.Path);
        Assert.IsTrue(_facts.Reads.All(file => !file.CanRead), "The scan must release its own readers before the fixture is changed.");

        // Facts keeps the identity and timestamps stable. Only the four content bytes change.
        await TestDirectory.RetrySharingViolationAsync(() => File.WriteAllText(candidate, "bbbb"));

        var bin = new Bin();
        var result = new CleanupService(bin, _facts).Execute(plan.Items).Single();
        Assert.IsFalse(result.Moved);
        Assert.AreEqual("The verified copy has changed.", result.Reason);
        Assert.IsEmpty(bin.Paths);
        Assert.IsTrue(File.Exists(keeper) && File.Exists(candidate));
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public async Task FixtureRewriteWaitsForAWindowsReader()
    {
        var candidate = Put("1", "candidate", "aaaa");
        Task rewrite;

        using (File.OpenRead(candidate))
        {
            rewrite = TestDirectory.RetrySharingViolationAsync(() => File.WriteAllText(candidate, "bbbb"));
            Assert.IsFalse(rewrite.IsCompleted, "The fixture cannot be rewritten while this reader denies writes.");
        }

        await rewrite;
        Assert.AreEqual("bbbb", File.ReadAllText(candidate));
    }

    [TestMethod]
    public async Task RechecksNamedStreamBytesBeforeRecycling()
    {
        var a = Put("5", "a", "same");
        var b = Put("1", "b", "same");
        WriteStream(a, "111");
        WriteStream(b, "111");
        var files = (await ScanAsync()).Files.Where(f => f.Path == a || f.Path == b);
        var plan = CleanupPlan.Create(files);
        await TestDirectory.RetrySharingViolationAsync(() => WriteStream(b, "222"));

        var bin = new Bin();
        Assert.IsFalse(new CleanupService(bin, _facts).Execute(plan.Items).Single().Moved);
        Assert.IsEmpty(bin.Paths);
    }

    [TestMethod]
    [DataRow(false, DisplayName = "A new stream on the candidate stops the shell handoff")]
    [DataRow(true, DisplayName = "A new stream on the keeper stops the shell handoff")]
    public async Task LastRecycleCheckRejectsAStreamAddedAfterTheInitialCheck(bool onKeeper)
    {
        var keeper = Put("9", "keep", "same");
        var candidate = Put("1", "copy", "same");
        var plan = CleanupPlan.Create((await ScanAsync()).Files);

        var changed = onKeeper ? keeper : candidate;
        var bin = new Bin(beforeValidate: () => _facts.Manifests[changed] = [new(":late:$DATA", 1)]);
        var result = new CleanupService(bin, _facts).Execute(plan.Items).Single();

        Assert.IsFalse(result.Moved);
        Assert.AreEqual("The stream list has changed.", result.Reason);
        Assert.IsEmpty(bin.Paths);
        Assert.IsTrue(File.Exists(candidate) && File.Exists(keeper));
    }

    [TestMethod]
    public async Task ExplicitManualSelectionCanRecycleASingleAndBinFailureKeepsIt()
    {
        var path = Put("5", "only", "data");
        var entry = (await ScanAsync()).Files.Single();
        var denied = new CleanupService(new Bin(() => throw new IOException("No recycle bin.")), _facts);
        var result = denied.Execute([new(entry, null)]).Single();
        Assert.IsFalse(result.Moved);
        Assert.IsTrue(File.Exists(path));

        var bin = new Bin();
        Assert.IsTrue(new CleanupService(bin, _facts).Execute([new(entry, null)]).Single().Moved);
        Assert.AreEqual(path, bin.Paths.Single());
    }

    [TestMethod]
    public async Task CancellationReportsItemsAlreadyCompleted()
    {
        Put("9", "keep", "same");
        Put("1", "low", "same");
        Put("2", "middle", "same");
        var plan = CleanupPlan.Create((await ScanAsync()).Files);
        using var stop = new CancellationTokenSource();
        var bin = new Bin(stop.Cancel);
        var results = new CleanupService(bin, _facts).Execute(plan.Items, token: stop.Token);
        Assert.HasCount(1, results);
        Assert.IsTrue(results[0].Moved);
        Assert.AreEqual(1, results[0].File.Root.Priority);
    }

    [TestMethod]
    public async Task CancelledScanDoesNotPublishPartialGroups()
    {
        Put("1", "a", "a");
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => ScanAsync(token: stop.Token));
    }

    [TestMethod]
    public void StreamManifestOmitsTheDefaultStreamAndSortsNamedStreams()
    {
        Assert.IsEmpty(WindowsFileFacts.ParseStreams(Entry("::$DATA", 12)));

        byte[] buffer =
        [
            .. Entry(":z:$DATA", 8, hasNext: true),
            .. Entry("::$DATA", 12, hasNext: true),
            .. Entry(":a:$DATA", 0)
        ];
        var streams = WindowsFileFacts.ParseStreams(buffer);
        CollectionAssert.AreEqual(new NamedStream[] { new(":a:$DATA", 0), new(":z:$DATA", 8) }, streams.ToArray());

        var truncated = Entry("::$DATA", 12, hasNext: true);
        Assert.Throws<IOException>(() => WindowsFileFacts.ParseStreams(truncated));

        static byte[] Entry(string name, long length, bool hasNext = false)
        {
            var nameBytes = Encoding.Unicode.GetBytes(name);
            const int headerSize = 24;
            var entry = new byte[(headerSize + nameBytes.Length + 7) / 8 * 8];
            if (hasNext) BinaryPrimitives.WriteInt32LittleEndian(entry, entry.Length);
            BinaryPrimitives.WriteInt32LittleEndian(entry.AsSpan(4), nameBytes.Length);
            BinaryPrimitives.WriteInt64LittleEndian(entry.AsSpan(8), length);
            nameBytes.CopyTo(entry, headerSize);
            return entry;
        }
    }

    [TestMethod]
    public void MalformedStreamBufferFailsClosed()
    {
        var buffer = new byte[40];
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(4), 128);
        Assert.Throws<IOException>(() => WindowsFileFacts.ParseStreams(buffer));
    }

    private sealed class Bin(Action? beforeReturn = null, Action? beforeValidate = null) : IRecycleBin
    {
        public List<string> Paths { get; } = [];
        public void Recycle(string path, Action validate)
        {
            beforeValidate?.Invoke();
            validate();
            beforeReturn?.Invoke();
            Paths.Add(path);
        }
    }

    private sealed class Stages(Action<ScanProgress>? onReport = null) : IProgress<ScanProgress>
    {
        internal List<ScanProgress> Updates { get; } = [];

        public void Report(ScanProgress value)
        {
            Updates.Add(value);
            onReport?.Invoke(value);
        }

        internal string[] Paths(string stage) => Updates.Where(update => update.Stage == stage).Select(update => update.Path).ToArray();
    }

    private sealed class Facts : IFileFacts
    {
        internal Dictionary<string, FileKey> Keys { get; } = [];
        internal Dictionary<string, uint> Links { get; } = [];
        internal Dictionary<string, IReadOnlyList<NamedStream>> Manifests { get; } = [];
        internal HashSet<string> Unreadable { get; } = [];
        internal HashSet<string> UnreadableSize { get; } = [];
        internal HashSet<string> Missing { get; } = [];
        internal List<FileStream> Reads { get; } = [];
        internal List<string> StreamReads { get; } = [];
        internal int ReadCount(string path) => Reads.Count(file => file.Name == path);

        public FileStamp Read(FileStream file)
        {
            Reads.Add(file);
            if (Missing.Contains(file.Name)) throw new FileNotFoundException("File no longer available.", file.Name);
            if (UnreadableSize.Contains(file.Name)) throw new IOException("Size unavailable.");
            if (!Keys.TryGetValue(file.Name, out var key)) Keys.Add(file.Name, key = new(1, Guid.NewGuid()));
            return new(key, file.Length, DateTime.UnixEpoch, 1, Links.GetValueOrDefault(file.Name, 1u));
        }
        public IReadOnlyList<NamedStream> Streams(FileStream file)
        {
            StreamReads.Add(file.Name);
            return Unreadable.Contains(file.Name)
                ? throw new IOException("Stream list unavailable.") : Manifests.GetValueOrDefault(file.Name) ?? [];
        }
    }

    private sealed class UnexpectedInspections : IChecksumService, ISignatureInspector
    {
        public Task<IReadOnlyDictionary<ChecksumAlgorithm, string>> ComputeAsync(Stream stream,
            IReadOnlyCollection<ChecksumAlgorithm> algorithms, IProgress<long>? progress = null, CancellationToken cancellationToken = default) =>
            throw new AssertFailedException("A file with a unique size reached the checksum service.");

        public SignatureResult Inspect(FileStream file, string fullPath, RevocationMode revocationMode, CancellationToken cancellationToken = default) =>
            throw new AssertFailedException("A file with a unique size reached the signature inspector.");
    }

    private sealed class Collision : IChecksumService
    {
        public Task<IReadOnlyDictionary<ChecksumAlgorithm, string>> ComputeAsync(Stream stream,
            IReadOnlyCollection<ChecksumAlgorithm> algorithms, IProgress<long>? progress = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<ChecksumAlgorithm, string>>(new Dictionary<ChecksumAlgorithm, string> { [ChecksumAlgorithm.SHA256] = "collision" });
    }
}
