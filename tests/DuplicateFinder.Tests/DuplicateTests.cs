using System.Buffers.Binary;
using System.Text;
using DuplicateFinder.Core;
using FileScope.Core.Abstractions;
using FileScope.Core.Checksums;
using FileScope.Core.Models;
using FileScope.Core.Signatures;

namespace DuplicateFinder.Tests;

[TestClass]
public sealed class DuplicateTests
{
    private readonly string _dir = Directory.CreateTempSubdirectory("DuplicateFinder-tests-").FullName;
    private readonly Facts _facts = new();
    private static readonly SearchOptions Options = new([ChecksumAlgorithm.MD5, ChecksumAlgorithm.SHA512], false, false, RevocationMode.OfflineCache);

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_dir, true);

    private string Put(string root, string name, string bytes)
    {
        var folder = Directory.CreateDirectory(Path.Combine(_dir, root)).FullName;
        var path = Path.Combine(folder, name);
        File.WriteAllText(path, bytes);
        return path;
    }

    private Task<ScanResult> ScanAsync(IChecksumService? service = null, SearchOptions? options = null,
        CancellationToken token = default)
    {
        var roots = Directory.GetDirectories(_dir).Select(d => new SearchRoot(d, int.Parse(Path.GetFileName(d),
            System.Globalization.CultureInfo.InvariantCulture))).ToArray();
        var scanner = new DuplicateScanner(_facts, service ?? new StreamChecksumService(new ChecksumStrategyFactory()),
            new WindowsAuthenticodeInspector());
        return scanner.ScanAsync(roots, options ?? Options, token: token);
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

        var options = Options with { SizeRange = new(2, 4), Embedded = true };
        var scan = await ScanAsync(options: options);
        Assert.IsEmpty(scan.Issues);
        CollectionAssert.AreEquivalent(admitted, scan.Files.Select(f => f.Path).ToArray());
        Assert.IsTrue(scan.Files.All(f => f.Hashes.ContainsKey(ChecksumAlgorithm.SHA256) && f.Embedded != SignatureResult.NotChecked));
        Assert.IsTrue(excluded.All(path => _facts.ReadCount[path] == 1), "Excluded files should only be opened for discovery.");

        var plan = CleanupPlan.Create(scan.Files);
        CollectionAssert.AreEquivalent(admitted.Where(path => Path.GetFileName(path).StartsWith("copy-", StringComparison.Ordinal)).ToArray(),
            plan.Items.Select(item => item.File.Path).ToArray());
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
        Put("10", "only", "unique");
        var scan = await ScanAsync();

        var all = CleanupPlan.Create(scan.Files);
        var filtered = CleanupPlan.Create(scan.Files, f => f.Path == download);
        CollectionAssert.AreEqual(new[] { download, backup }, all.Items.Select(i => i.File.Path).ToArray());
        Assert.AreEqual(1, filtered.Groups);
        Assert.AreEqual(download, filtered.Items.Single().File.Path);
        Assert.AreEqual(30, filtered.Items.Single().Keeper!.Root.Priority);
        Assert.AreEqual(3, scan.Files.Single(f => f.Path == backup).Hashes.Count);
        Assert.IsEmpty(scan.Files.Single(f => f.Name == "only").Hashes);
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

        var scan = await ScanAsync(new Collision());
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
        File.WriteAllText(candidate, "different length");
        Assert.IsFalse(cleanup.Execute(plan.Items).Single().Moved);

        File.WriteAllText(candidate, "same");
        var fresh = CleanupPlan.Create((await ScanAsync()).Files);
        File.Move(keeper, keeper + ".moved");
        Assert.IsFalse(cleanup.Execute(fresh.Items).Single().Moved);
        Assert.IsEmpty(bin.Paths);
        Assert.IsTrue(File.Exists(candidate));
    }

    [TestMethod]
    public async Task BinaryRecheckRejectsContentEvenWhenTheScanStampLooksIdentical()
    {
        var a = Put("9", "keep", "aaaa");
        var b = Put("1", "candidate", "aaaa");
        var plan = CleanupPlan.Create((await ScanAsync()).Files);
        File.WriteAllText(b, "bbbb");
        var bin = new Bin();
        Assert.IsFalse(new CleanupService(bin, _facts).Execute(plan.Items).Single().Moved);
        Assert.IsEmpty(bin.Paths);
        Assert.IsTrue(File.Exists(a));
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
        WriteStream(b, "222");

        var bin = new Bin();
        Assert.IsFalse(new CleanupService(bin, _facts).Execute(plan.Items).Single().Moved);
        Assert.IsEmpty(bin.Paths);
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
    public void MalformedStreamBufferFailsClosed()
    {
        var buffer = new byte[40];
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(4), 128);
        Assert.Throws<IOException>(() => WindowsFileFacts.ParseStreams(buffer));
    }

    private sealed class Bin(Action? beforeReturn = null) : IRecycleBin
    {
        public List<string> Paths { get; } = [];
        public void Recycle(string path, Action validate)
        {
            validate();
            beforeReturn?.Invoke();
            Paths.Add(path);
        }
    }

    private sealed class Facts : IFileFacts
    {
        internal Dictionary<string, FileKey> Keys { get; } = [];
        internal Dictionary<string, uint> Links { get; } = [];
        internal Dictionary<string, IReadOnlyList<NamedStream>> Manifests { get; } = [];
        internal HashSet<string> Unreadable { get; } = [];
        internal HashSet<string> UnreadableSize { get; } = [];
        internal Dictionary<string, int> ReadCount { get; } = [];
        public FileStamp Read(FileStream file)
        {
            ReadCount[file.Name] = ReadCount.GetValueOrDefault(file.Name) + 1;
            if (UnreadableSize.Contains(file.Name)) throw new IOException("Size unavailable.");
            if (!Keys.TryGetValue(file.Name, out var key)) Keys.Add(file.Name, key = new(1, Guid.NewGuid()));
            return new(key, file.Length, DateTime.UnixEpoch, 1, Links.GetValueOrDefault(file.Name, 1u));
        }
        public IReadOnlyList<NamedStream> Streams(FileStream file) => Unreadable.Contains(file.Name)
            ? throw new IOException("Stream list unavailable.") : Manifests.GetValueOrDefault(file.Name) ?? [];
    }

    private sealed class Collision : IChecksumService
    {
        public Task<IReadOnlyDictionary<ChecksumAlgorithm, string>> ComputeAsync(Stream stream,
            IReadOnlyCollection<ChecksumAlgorithm> algorithms, IProgress<long>? progress = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<ChecksumAlgorithm, string>>(new Dictionary<ChecksumAlgorithm, string> { [ChecksumAlgorithm.SHA256] = "collision" });
    }
}
