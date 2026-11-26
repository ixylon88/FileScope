using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using DuplicateFinder.Core;
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
                new([], false, false, RevocationMode.OfflineCache));
            Assert.HasCount(2, scan.Files);
            Assert.HasCount(1, scan.Files.Where(f => f.IsHardLink).ToArray());
            Assert.IsTrue(scan.Files.All(f => f.Group > 0 && f.Streams == StreamComparison.Different));
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
            var members = Directory.CreateDirectory(Path.Combine(root.FullName, "members")).FullName;
            var member = Path.Combine(members, "unique.txt");
            File.WriteAllText(member, Guid.NewGuid().ToString());
            var cat = Path.Combine(root.FullName, "loose.cat");

            // This built-in Windows PowerShell command creates an unsigned catalog.
            // Its member must be discoverable, but must never gain trusted status.
            var shell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            var start = new ProcessStartInfo(shell) { UseShellExecute = false, CreateNoWindow = true };
            start.Environment["DF_TEST_MEMBERS"] = members;
            start.Environment["DF_TEST_CAT"] = cat;
            start.ArgumentList.Add("-NoProfile");
            start.ArgumentList.Add("-NonInteractive");
            start.ArgumentList.Add("-Command");
            start.ArgumentList.Add("$ErrorActionPreference='Stop'; New-FileCatalog -Path $env:DF_TEST_MEMBERS -CatalogFilePath $env:DF_TEST_CAT -CatalogVersion 2.0 | Out-Null");
            using var process = Process.Start(start)!;
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(1));
            Assert.AreEqual(0, process.ExitCode);

            var errors = new List<string>();
            var index = new WindowsCatalogIndex([cat], (path, reason) => errors.Add(reason));
            using (var file = File.OpenRead(member))
            {
                var found = index.Inspect(file, RevocationMode.OfflineCache);
                Assert.IsEmpty(errors, string.Join(Environment.NewLine, errors));
                var match = found.Matches.Single(m => m.Path == cat);
                Assert.IsFalse(match.Registered);
                Assert.AreNotEqual(SignatureStatus.Valid, match.Trust.Status);
            }

            File.WriteAllText(member, Guid.NewGuid().ToString());
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
