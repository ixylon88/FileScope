using DuplicateFinder.Core.Models;

namespace DuplicateFinder.Tests;

public sealed partial class DuplicateTests
{
    [TestMethod]
    public async Task DifferentStreamNamesOrLengthsNeedNoPairwiseFileOpensAfterTheMainBytesMatch()
    {
        const int lengths = 4;
        const int fileCount = 2 * lengths;
        var main = new string('x', 64);
        for (var length = 1; length <= lengths; length++)
        {
            var content = new string('s', length);
            WriteStream(Put("1", $"notes-{length}", main), content, ":notes:$DATA");
            WriteStream(Put("1", $"origin-{length}", main), content, ":origin:$DATA");
        }

        int? readsBeforeStreamComparison = null;
        var progress = new Stages(update =>
        {
            if (update.Stage == "Compare" && _facts.StreamReads.Count == fileCount)
                readsBeforeStreamComparison ??= _facts.Reads.Count;
        });
        var scan = await ScanAsync(options: Options with { SizeRange = new(main.Length, main.Length) }, progress: progress);

        Assert.HasCount(fileCount, scan.Files);
        Assert.AreEqual(1, scan.Files.Select(file => file.Group).Distinct().Count());
        Assert.IsTrue(scan.Files.All(file => file.Group > 0 && file.Streams == StreamComparison.Different));
        Assert.AreEqual(fileCount, scan.Files.Select(file => file.Variant).Distinct().Count());
        Assert.AreEqual(_facts.Reads.Count, readsBeforeStreamComparison);
        Assert.IsEmpty(CleanupPlan.Create(scan.Files).Items);
    }

    [TestMethod]
    public async Task AHashCollisionCannotHideADifferentLastByteOfTheMainStream()
    {
        var prefix = new string('x', 128 * 1024);
        var original = Put("9", "original", prefix + "A");
        var copy = Put("5", "copy", prefix + "A");
        var different = Put("1", "different", prefix + "B");
        WriteStream(original, "one");
        WriteStream(copy, "longer");
        WriteStream(different, "one");

        var length = prefix.Length + 1;
        var scan = await ScanAsync(new Collision(), Options with { SizeRange = new(length, length) });
        var files = scan.Files.ToDictionary(file => file.Path);

        Assert.IsGreaterThan(0, files[original].Group);
        Assert.AreEqual(files[original].Group, files[copy].Group);
        Assert.AreNotEqual(files[original].Variant, files[copy].Variant);
        Assert.AreEqual(0, files[different].Group);
        Assert.IsEmpty(CleanupPlan.Create(scan.Files).Items);
    }
}
