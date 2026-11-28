using DuplicateFinder.Core.Files;
using DuplicateFinder.Core.Models;

namespace DuplicateFinder.Tests;

public sealed partial class DuplicateTests
{
    [TestMethod]
    [DataRow(true, DisplayName = "A read failure belongs to the open reference file")]
    [DataRow(false, DisplayName = "A read failure belongs to the open candidate file")]
    public void BinaryReadErrorIdentifiesItsOwner(bool onReference)
    {
        var root = new SearchRoot("root", 1);
        var reference = new FoundFile("reference", root);
        var candidate = new FoundFile("candidate", root);
        using var readable = new MemoryStream([1]);
        using var unreadable = new UnreadableStream();

        var error = Assert.ThrowsExactly<ComparisonReadException>(() => BinaryComparison.Equal(
            new(reference, onReference ? unreadable : readable),
            new(candidate, onReference ? readable : unreadable), CancellationToken.None));

        Assert.AreSame(onReference ? reference : candidate, error.File);
        Assert.AreSame(unreadable.Error, error.InnerException);
    }

    [TestMethod]
    [DataRow(false, DisplayName = "Reference disappears before the first pair is confirmed")]
    [DataRow(true, DisplayName = "Reference disappears after a pair has been confirmed")]
    public async Task LosingTheReferenceStillGroupsTheTwoReadableCopies(bool firstPairConfirmed)
    {
        var reference = Put("9", "reference", "same");
        var backup = Put("5", "backup", "same");
        var copy = Put("1", "copy", "same");
        var trigger = firstPairConfirmed ? copy : backup;
        var progress = new Stages(update =>
        {
            if (update.Stage == "Compare" && update.Path == trigger) _facts.Missing.Add(reference);
        });

        var scan = await ScanAsync(progress: progress);
        var missing = scan.Files.Single(file => file.Path == reference);
        var keeper = scan.Files.Single(file => file.Path == backup);
        var candidate = scan.Files.Single(file => file.Path == copy);

        Assert.IsNotNull(missing.Diagnostic);
        Assert.AreEqual(0, missing.Group);
        Assert.IsNull(keeper.Diagnostic);
        Assert.IsNull(candidate.Diagnostic);
        Assert.IsGreaterThan(0, keeper.Group);
        Assert.AreEqual(keeper.Group, candidate.Group);
        var removal = CleanupPlan.Create(scan.Files).Items.Single();
        Assert.AreSame(keeper, removal.Keeper);
        Assert.AreSame(candidate, removal.File);
    }

    [TestMethod]
    public async Task ReplacementReferenceStillRequiresMatchingBytesAfterAHashCollision()
    {
        var original = Put("9", "original", "aaaa");
        var backup = Put("7", "backup", "aaaa");
        var collision = Put("5", "collision", "bbbb");
        var otherCopy = Put("3", "other-copy", "bbbb");
        var copy = Put("1", "copy", "aaaa");
        var progress = new Stages(update =>
        {
            if (update.Stage == "Compare" && update.Path == collision) _facts.Missing.Add(original);
        });

        var scan = await ScanAsync(new Collision(), progress: progress);
        var files = scan.Files.ToDictionary(file => file.Path);

        Assert.IsNotNull(files[original].Diagnostic);
        var plan = CleanupPlan.Create(scan.Files);
        Assert.HasCount(2, plan.Items);
        Assert.AreSame(files[backup], plan.Items.Single(item => item.File.Path == copy).Keeper);
        Assert.AreSame(files[collision], plan.Items.Single(item => item.File.Path == otherCopy).Keeper);
    }

    [TestMethod]
    [DataRow(true, DisplayName = "Only the reference has an unreadable named stream")]
    [DataRow(false, DisplayName = "Only the candidate has an unreadable named stream")]
    public async Task AnUnreadableNamedStreamLeavesTheOtherFilesInTheSameVerifiedVariant(bool onReference)
    {
        var first = Put("9", "first", "same");
        var second = Put("5", "second", "same");
        var last = Put("1", "last", "same");
        var unreadable = onReference ? first : second;
        var readable = onReference ? second : first;

        WriteStream(readable, "ads");
        WriteStream(last, "ads");
        // The manifest was available, but opening that stream for the byte comparison fails.
        _facts.Manifests[unreadable] = _facts.Manifests[readable];

        var scan = await ScanAsync(options: Options with { SizeRange = new(4, 4) });
        var files = scan.Files.ToDictionary(file => file.Path);

        Assert.IsNotNull(files[unreadable].Diagnostic);
        Assert.AreEqual(StreamComparison.Unknown, files[unreadable].Streams);
        Assert.AreEqual(0, files[unreadable].Variant);
        Assert.IsNull(files[readable].Diagnostic);
        Assert.IsNull(files[last].Diagnostic);
        Assert.IsGreaterThan(0, files[readable].Variant);
        Assert.AreEqual(files[readable].Variant, files[last].Variant);
    }

    private sealed class UnreadableStream() : MemoryStream([1])
    {
        internal IOException Error { get; } = new("Read interrupted.");
        public override int Read(Span<byte> buffer) => throw Error;
    }
}
