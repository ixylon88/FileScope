using DuplicateFinder.Core.Models;
using DuplicateFinder.Core.Services;
using FileScope.TestSupport;

namespace DuplicateFinder.Tests;

public sealed partial class DuplicateTests
{
    [TestMethod]
    public async Task AMovedKeeperCanBeReplacedByATiedCopyOutsideTheCleanupFilter()
    {
        var (item, other) = await PlanWithTiedKeepers();
        var preferred = item.Keeper!.Path;
        await TestDirectory.RetrySharingViolationAsync(() => File.Move(preferred, preferred + ".moved"));

        var bin = new Bin();
        var result = new CleanupService(bin, _facts).Execute([item]).Single();

        Assert.IsTrue(result.Moved, result.Reason);
        Assert.AreEqual(item.File.Path, bin.Paths.Single());
        Assert.IsTrue(File.Exists(other.Path));
    }

    [TestMethod]
    [DataRow(false, DisplayName = "The alternate keeper's main bytes must still match")]
    [DataRow(true, DisplayName = "The alternate keeper's named stream bytes must still match")]
    public async Task AnAlternateKeeperWithChangedBytesNeverReachesTheBin(bool changeNamedStream)
    {
        var (item, other) = await PlanWithTiedKeepers();
        _facts.Missing.Add(item.Keeper!.Path);
        await TestDirectory.RetrySharingViolationAsync(() =>
        {
            // The fixture preserves size and scan stamp, so only the corresponding byte comparison can reject it.
            if (changeNamedStream) WriteStream(other.Path, "new");
            else File.WriteAllText(other.Path, "edit");
        });

        var previousReads = _facts.ReadCount(other.Path);
        var bin = new Bin();
        var result = new CleanupService(bin, _facts).Execute([item]).Single();

        Assert.IsGreaterThan(previousReads, _facts.ReadCount(other.Path));
        Assert.IsFalse(result.Moved);
        Assert.IsEmpty(bin.Paths);
    }

    [TestMethod]
    public async Task AnUnavailableTopTierNeverPromotesALowerPriorityCopyToKeeper()
    {
        var (item, other) = await PlanWithTiedKeepers();
        _facts.Missing.Add(item.Keeper!.Path);
        _facts.Missing.Add(other.Path);

        var bin = new Bin();
        var result = new CleanupService(bin, _facts).Execute([item]).Single();

        Assert.IsFalse(result.Moved);
        Assert.IsNotNull(result.Reason);
        Assert.IsEmpty(bin.Paths);
    }

    [TestMethod]
    public async Task TheFinalShellGuardRechecksTheSelectedAlternateKeeper()
    {
        var (item, other) = await PlanWithTiedKeepers();
        _facts.Missing.Add(item.Keeper!.Path);
        var reachedHandoff = false;
        var bin = new Bin(beforeValidate: () =>
        {
            reachedHandoff = true;
            _facts.Manifests[other.Path] = [new(":unexpected:$DATA", 1)];
        });

        var result = new CleanupService(bin, _facts).Execute([item]).Single();

        Assert.IsTrue(reachedHandoff);
        Assert.IsFalse(result.Moved);
        Assert.IsEmpty(bin.Paths);
    }

    [TestMethod]
    public async Task AShellFailureDoesNotTriggerAnotherRecycleAttempt()
    {
        var (item, _) = await PlanWithTiedKeepers();
        var attempts = 0;
        var bin = new Bin(beforeReturn: () =>
        {
            attempts++;
            throw new IOException("The shell did not confirm the handoff.");
        });

        var result = new CleanupService(bin, _facts).Execute([item]).Single();

        Assert.IsFalse(result.Moved);
        Assert.AreEqual(1, attempts);
    }

    private async Task<(CleanupItem Item, FoundFile Other)> PlanWithTiedKeepers()
    {
        var archiveA = Put("9", "archive-a", "same");
        var archiveB = Put("9", "archive-b", "same");
        var middle = Put("5", "middle", "same");
        var candidate = Put("1", "candidate", "same");
        foreach (var path in new[] { archiveA, archiveB, middle, candidate }) WriteStream(path, "ads");

        var scan = await ScanAsync(options: Options with { SizeRange = new(4, 4) });
        var item = CleanupPlan.Create(scan.Files, file => file.Path == candidate).Items.Single();
        var other = scan.Files.Single(file => file.Root.Priority == 9 && file != item.Keeper);
        return (item, other);
    }
}
