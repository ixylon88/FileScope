using DuplicateFinder.Core.Files;
using DuplicateFinder.Core.Models;

namespace DuplicateFinder.Tests;

public sealed partial class DuplicateTests
{
    [TestMethod]
    [DataRow(9, 1, DisplayName = "The parent folder has higher priority")]
    [DataRow(1, 9, DisplayName = "The nested folder has higher priority")]
    [DataRow(5, 5, DisplayName = "Equal priorities preserve the first selected folder")]
    public async Task OverlappingRootsListEachDirectoryOnce(int parentPriority, int nestedPriority)
    {
        var photo = Put("archive/photos/2025", "photo", "one");
        var sibling = Put("archive/photos-old", "older", "other");
        var parent = Path.Combine(_dir, "archive");
        var nested = Path.Combine(parent, "photos");
        var leaf = Path.GetDirectoryName(photo)!;

        var progress = new Stages();
        var scan = await ScanAsync(progress: progress, roots: [new(nested + Path.DirectorySeparatorChar, nestedPriority),
            new(parent, parentPriority)]);

        Assert.IsEmpty(scan.Issues);
        CollectionAssert.AreEquivalent(new[] { photo, sibling }, scan.Files.Select(file => file.Path).ToArray());
        Assert.AreEqual(1, progress.Paths("Search").Count(path => path == leaf), "The leaf directory is listed once.");
        Assert.AreEqual(2, progress.Updates.Last(update => update.Stage == "Search").Completed);

        var photoRoot = scan.Files.Single(file => file.Path == photo).Root;
        Assert.AreEqual(Math.Max(parentPriority, nestedPriority), photoRoot.Priority);
        Assert.AreEqual(nestedPriority >= parentPriority ? nested : parent, Path.TrimEndingDirectorySeparator(photoRoot.Path));
        Assert.AreEqual(parent, scan.Files.Single(file => file.Path == sibling).Root.Path);
    }

    [TestMethod]
    public async Task DirectoryCasingPreservesDistinctFoldersAndCoalescesAliases()
    {
        Put("archive/Photos/leaf", "first", "one");
        var archive = Path.Combine(_dir, "archive");
        var lowerCase = Path.Combine(archive, "photos");
        var sameDirectory = Directory.Exists(lowerCase);
        if (!sameDirectory) Put("archive/photos/leaf", "second", "longer");

        var progress = new Stages();
        var scan = await ScanAsync(progress: progress, roots: [new(lowerCase, 9), new(archive, 1)]);

        Assert.IsEmpty(scan.Issues);
        var expectedDirectories = sameDirectory ? 1 : 2;
        Assert.AreEqual(expectedDirectories, progress.Paths("Search").Count(path => Path.GetFileName(path) == "leaf"));
        Assert.HasCount(expectedDirectories, scan.Files);
        Assert.AreEqual(sameDirectory ? 9 : 1, scan.Files.Single(file => file.Name == "first").Root.Priority);
        if (!sameDirectory) Assert.AreEqual(9, scan.Files.Single(file => file.Name == "second").Root.Priority);
    }

    [TestMethod]
    public void CaseOnlyDirectoryMatchesRequireTheSameKnownIdentity()
    {
        var first = new FileKey(1, Guid.NewGuid());
        var other = new FileKey(1, Guid.NewGuid());
        var reads = new List<string>();
        var comparer = new DirectoryPathComparer(path =>
        {
            reads.Add(path);
            return path switch { "Photos" or "PHOTOS" => first, "photos" => other, _ => null };
        });

        Assert.IsTrue(comparer.Equals("Photos", "Photos"));
        Assert.IsFalse(comparer.Equals("Photos", "Photos-old"));
        Assert.IsEmpty(reads);

        Assert.IsTrue(comparer.Equals("Photos", "PHOTOS"));
        Assert.IsFalse(comparer.Equals("Photos", "photos"));
        Assert.IsFalse(comparer.Equals("Photos", "pHoToS"));
        Assert.IsTrue(comparer.Equals("PHOTOS", "Photos"));
        CollectionAssert.AreEquivalent(new[] { "Photos", "PHOTOS", "photos", "pHoToS" }, reads);
    }

    [TestMethod]
    public async Task OverlappingRootsRetryAnIncompleteSubtreeAndSkipItsCompletedSibling()
    {
        var ready = Put("archive/photos/ready", "ready.bin", "one");
        var delayed = Put("archive/photos/pending", "pending.bin", "later");
        var parent = Path.Combine(_dir, "archive");
        var nested = Path.Combine(parent, "photos");
        var pending = Path.GetDirectoryName(delayed)!;
        var aside = Path.Combine(_dir, "temporarily-unavailable");
        var interrupted = false;

        var progress = new Stages(update =>
        {
            if (!interrupted && update.Stage == "Search" && update.Path == pending)
            {
                Directory.Move(pending, aside);
                interrupted = true;
            }
            else if (update.Stage == "Metadata" && update.Path == ready && Directory.Exists(aside))
            {
                Directory.Move(aside, pending);
            }
        });

        var scan = await ScanAsync(progress: progress, roots: [new(nested, 9), new(parent, 1)]);

        Assert.AreEqual(pending, scan.Issues.Single().Path);
        Assert.AreEqual(2, progress.Paths("Search").Count(path => path == pending));
        Assert.AreEqual(1, progress.Paths("Search").Count(path => path == Path.GetDirectoryName(ready)));
        CollectionAssert.AreEquivalent(new[] { ready, delayed }, scan.Files.Select(file => file.Path).ToArray());
    }
}
