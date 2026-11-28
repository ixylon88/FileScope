namespace DuplicateFinder.Tests;

public sealed partial class DuplicateTests
{
    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    [DataRow(null, DisplayName = "One admitted file leaves the loose catalog closed")]
    [DataRow("diff", DisplayName = "A hash collision without equal bytes leaves the loose catalog closed")]
    [DataRow("same", DisplayName = "The first byte-confirmed group opens the catalog once")]
    public async Task OutOfRangeCatalogsOpenOnlyForAConfirmedGroup(string? secondContents)
    {
        Put("9", "first", "same");
        if (secondContents != null) Put("5", "second", secondContents);
        var catalog = Path.Combine(_dir, "outside.CaT");
        WindowsTests.WriteLooseCatalog(catalog);

        var progress = new Stages(update =>
        {
            if (update.Stage == "CatalogIndex" && update.Path == catalog)
                Assert.AreEqual(0, _facts.ReadCount(catalog), "Discovery must not open an out-of-range catalog.");
        });

        var options = Options with { Catalogs = true, SizeRange = new(4, 4) };
        var scan = await ScanAsync(new Collision(), options, progress, roots: [new(_dir, 1)]);

        var needsCatalog = secondContents == "same";
        Assert.AreEqual(needsCatalog ? 1 : 0, _facts.ReadCount(catalog));
        Assert.AreEqual(needsCatalog ? 1 : 0, progress.Paths("CatalogIndex").Length);
        Assert.IsFalse(progress.Paths("Metadata").Contains(catalog));
        Assert.IsFalse(scan.Files.Any(file => file.Path == catalog));
        Assert.IsEmpty(scan.Issues);
        Assert.IsTrue(scan.Files.All(file => (file.Catalogs != null) == needsCatalog));
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public async Task AnUnreadableDeferredCatalogMarksTheIndexIncompleteForEveryMember()
    {
        Put("9", "first", "same");
        Put("1", "second", "same");
        var catalog = Put("5", "unreadable.cat", "outside the intake range");
        _facts.UnreadableSize.Add(catalog);

        var scan = await ScanAsync(options: Options with { Catalogs = true, SizeRange = new(4, 4) });

        Assert.AreEqual(catalog, scan.Issues.Single().Path);
        Assert.AreEqual(1, _facts.ReadCount(catalog));
        Assert.HasCount(2, scan.Files);
        Assert.IsTrue(scan.Files.All(file => file.Group > 0 && file.Catalogs is { IndexComplete: false }));
    }
}
