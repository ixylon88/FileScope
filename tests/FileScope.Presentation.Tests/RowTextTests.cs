using System.Collections;
using FileScope.App.ViewModels;
using FileScope.Core.Models;

namespace FileScope.Presentation.Tests;

public sealed partial class PresentationTests
{
    [TestMethod]
    public void SearchFormatsCatalogDetailsOnlyWhenOtherFieldsMiss()
    {
        var strings = LocalizationService.Instance;
        var issues = new CountingIssues("catalog-only message");
        var row = new FileRowViewModel("member.bin", strings);
        var result = new FileAnalysisResult(row.FullPath, 2, null,
            new Dictionary<ChecksumAlgorithm, string> { [ChecksumAlgorithm.SHA256] = "00FF" }, SignatureResult.NotChecked)
        {
            Properties = new(new FilePropertiesInfo { FileDescription = "metadata-only match" }),
            Catalogs = new([], issues, true)
        };
        row.Complete(result);

        Assert.IsTrue(row.Matches("MEMBER.BIN"));
        Assert.IsTrue(row.Matches("00ff"));
        Assert.IsTrue(row.Matches("metadata-only"));
        Assert.AreEqual(0, issues.Enumerations);

        Assert.IsTrue(row.Matches("catalog-only"));
        var first = row.CatalogDetails;
        Assert.IsFalse(row.Matches("no such value"));
        Assert.Contains(first, row.Details);
        row.SetHashFormat(HashFormat.Base64);
        Assert.AreSame(first, row.CatalogDetails);
        Assert.AreEqual(1, issues.Enumerations);

        strings.SetCulture("de");
        Assert.Contains(strings["Incomplete"], row.CatalogDetails);
        Assert.AreEqual(2, issues.Enumerations);

        row.Complete(result with { Catalogs = new([], ["replacement catalog"], true) });
        Assert.IsTrue(row.Matches("replacement catalog"));
        Assert.IsFalse(row.Matches("catalog-only"));
        row.Reset();
        Assert.IsFalse(row.Matches("replacement catalog"));
    }

    [TestMethod]
    public void SelectedDetailsChangeOncePerStageAndSurvivePercentageUpdates()
    {
        var strings = LocalizationService.Instance;
        var row = new FileRowViewModel("sample.bin", strings);
        Assert.Contains(strings["Row_Queued"], row.Details);
        row.Start(RevocationMode.Online);
        Assert.Contains(strings["OpeningFile"], row.Details);

        var updates = new List<string>();
        row.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName == nameof(row.Details)) updates.Add(row.Details);
        };
        row.SetProgress(new(AnalysisStage.Reading, 1, 10), 10);
        Assert.HasCount(1, updates);
        Assert.Contains(strings["Reading"], updates[0]);
        row.SetProgress(new(AnalysisStage.Reading, 2, 10), 20);
        Assert.HasCount(1, updates);
        Assert.AreSame(updates[0], row.Details);

        row.SetProgress(new(AnalysisStage.CheckingSignature, 2, 10), 20);
        Assert.HasCount(2, updates);
        Assert.Contains(strings["CheckingSignatureOnline"], updates[1]);
        row.Cancel();
        Assert.Contains(strings["Row_Cancelled"], row.Details);
        row.Reset();
        Assert.Contains(strings["Row_Queued"], row.Details);
    }

    private sealed class CountingIssues(params string[] issues) : IReadOnlyList<string>
    {
        public int Enumerations { get; private set; }
        public int Count => issues.Length;
        public string this[int index] => issues[index];

        public IEnumerator<string> GetEnumerator()
        {
            Enumerations++;
            return ((IEnumerable<string>)issues).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
