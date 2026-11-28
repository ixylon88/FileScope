using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using DuplicateFinder.Core.Files;
using DuplicateFinder.Core.Models;
using DuplicateFinder.Core.Services;
using FileScope.Core.Models;
using FileScope.Core.Signatures;

namespace DuplicateFinder.Tests;

[TestClass]
[OSCondition(OperatingSystems.Windows)]
public sealed partial class WindowsTests
{
    [TestMethod]
    public async Task NativeScanRecognizesHardlinksAndNamedStreamVariants()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Windows file identities and NTFS streams.");
            return;
        }
        var root = Directory.CreateTempSubdirectory("DuplicateFinder-NTFS-");
        try
        {
            var high = Directory.CreateDirectory(Path.Combine(root.FullName, "high")).FullName;
            var low = Directory.CreateDirectory(Path.Combine(root.FullName, "low")).FullName;
            var a = Path.Combine(high, "a");
            var b = Path.Combine(low, "b");
            File.WriteAllText(a, "same");
            File.WriteAllText(b, "same");
            File.WriteAllText(b + ":notes", "extra");
            if (CreateHardLinkW(Path.Combine(high, "another-name"), a, 0) == 0) throw new Win32Exception();

            var scan = await new DuplicateScanner().ScanAsync([new(high, 10), new(low, 1)],
                new([], false, true, RevocationMode.OfflineCache));
            Assert.HasCount(2, scan.Files);
            Assert.HasCount(1, scan.Files.Where(f => f.IsHardLink).ToArray());
            Assert.IsTrue(scan.Files.All(f => f.Group > 0 && f.Streams == StreamComparison.Different));
            Assert.IsTrue(scan.Files.All(f => f.Catalogs != null), "Confirmed copies also query registered catalogs when the search folders contain no .cat files.");
            Assert.IsEmpty(CleanupPlan.Create(scan.Files).Items);
        }
        finally { root.Delete(true); }
    }

    [TestMethod]
    public async Task LooseCatalogIdentifiesOnlyItsMemberWithoutInstallingTheCatalog()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Windows catalog provider.");
            return;
        }
        var root = Directory.CreateTempSubdirectory("DuplicateFinder-catalog-");
        try
        {
            // Fixtures/loose.cat is an unsigned V2 catalog built with the .NET Arcade
            // CatalogBuilder from precisely these UTF-8 bytes (no BOM or newline).
            var original = "DuplicateFinder catalog membership regression fixture."u8.ToArray();
            var member = Path.Combine(root.FullName, "member.txt");
            File.WriteAllBytes(member, original);
            var copy = Path.Combine(root.FullName, "member-copy.txt");
            File.Copy(member, copy);
            var cat = Path.Combine(root.FullName, "loose.CaT");
            WriteLooseCatalog(cat);
            var alias = Path.Combine(root.FullName, "same-catalog.cat");
            if (CreateHardLinkW(alias, cat, 0) == 0) throw new Win32Exception();

            // The catalog is outside the intake range and still describes the admitted file.
            var options = new SearchOptions([], false, true, RevocationMode.OfflineCache)
            {
                SizeRange = new(original.Length, original.Length)
            };
            var scan = await new DuplicateScanner().ScanAsync([new(root.FullName, 1)], options);
            Assert.IsEmpty(scan.Issues, string.Join(Environment.NewLine, scan.Issues));
            Assert.HasCount(2, scan.Files);
            var row = scan.Files.Single(f => f.Path == member);
            Assert.IsGreaterThan(0, row.Group);
            Assert.AreEqual(row.Group, scan.Files.Single(f => f.Path == copy).Group);
            var found = row.Catalogs;
            Assert.IsNotNull(found, row.Diagnostic);
            Assert.IsTrue(found.IndexComplete);
            Assert.IsEmpty(found.Issues, string.Join(Environment.NewLine, found.Issues));
            var sha256 = Convert.ToHexString(SHA256.HashData(original));
            var match = found.Matches.SingleOrDefault(m => (m.Path == cat || m.Path == alias)
                && string.Equals(m.Member, sha256, StringComparison.OrdinalIgnoreCase));
            Assert.IsNotNull(match, $"Expected SHA-256 catalog member {sha256}. Returned tags: {string.Join(", ", found.Matches.Select(m => m.Member))}");
            Assert.IsFalse(match.Registered);
            Assert.AreNotEqual(SignatureStatus.Valid, match.Trust.Status);

            original[^1] ^= 1;
            File.WriteAllBytes(member, original);
            var errors = new List<string>();
            var index = new WindowsCatalogIndex([cat], (path, reason) => errors.Add(reason));
            Assert.IsEmpty(errors, string.Join(Environment.NewLine, errors));
            using var changed = File.OpenRead(member);
            var absent = index.Inspect(changed, RevocationMode.OfflineCache);
            Assert.IsTrue(absent.IndexComplete);
            Assert.IsEmpty(absent.Issues, string.Join(Environment.NewLine, absent.Issues));
            Assert.IsEmpty(absent.Matches);

            var invalidIndex = new WindowsCatalogIndex([member + ".missing"], (path, reason) => errors.Add(reason));
            Assert.IsFalse(invalidIndex.Inspect(changed, RevocationMode.OfflineCache).IndexComplete);
        }
        finally { root.Delete(true); }
    }

    internal static void WriteLooseCatalog(string path)
    {
        using var resource = typeof(WindowsTests).Assembly.GetManifestResourceStream("DuplicateFinder.Tests.loose.cat");
        using var output = File.Create(path);
        Assert.IsNotNull(resource);
        resource.CopyTo(output);
    }

    [STATestMethod]
    public void ShellRecycleReturnsARecycleBinItemAndCancellableGuardKeepsSource()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Windows shell and per-user recycle bin.");
            return;
        }
        CheckShellRecycle();
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void CheckShellRecycle()
    {
        var root = Directory.CreateTempSubdirectory("DuplicateFinder-shell-");
        try
        {
            var path = Path.Combine(root.FullName, $"DuplicateFinder-recycle-test-{Guid.NewGuid():N}.txt");
            File.WriteAllText(path, "Disposable DuplicateFinder integration test.");
            var bin = new WindowsRecycleBin();
            Assert.Throws<OperationCanceledException>(() => bin.Recycle(path, () => throw new OperationCanceledException()));
            Assert.IsTrue(File.Exists(path));

            var facts = new WindowsFileFacts();
            var entry = new FoundFile(path, new(root.FullName, 1));
            using (var opened = File.OpenRead(path)) entry.Stamp = facts.Read(opened);
            var result = new CleanupService(bin).Execute([new(entry, null)]).Single();
            Assert.IsTrue(result.Moved, result.Reason);
            Assert.IsFalse(File.Exists(path));
        }
        finally { root.Delete(true); }
    }

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int CreateHardLinkW(string link, string existing, nint security);
}
