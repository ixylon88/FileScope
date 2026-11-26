using System.ComponentModel;
using System.Windows.Data;
using DuplicateFinder.Core;
using FileScope.App.Infrastructure;
using FileScope.App.Localization;
using FileScope.App.Presentation;
using FileScope.Core.Models;

namespace DuplicateFinder.App;

public sealed class ResultRow(FoundFile file) : ObservableObject
{
    private static LocalizationService Strings => LocalizationService.Instance;
    public FoundFile File { get; } = file;
    public bool Marked { get; set => SetProperty(ref field, value); }
    internal string? RecycleError { get; set; }
    public string? Diagnostic => RecycleError ?? File.Diagnostic;
    public int CurrentGroup { get; internal set; }
    public string GroupTitle => CurrentGroup == 0 ? Strings["Singles"] : Strings.Format("GroupNumber", File.Group);
    public string Streams => Strings["Streams_" + (CurrentGroup == 0 ? StreamComparison.Unmatched : File.Streams)];
    private static string FormatTrust(SignatureResult trust) =>
        SignatureGroup.From(trust.Status, trust.RevocationMode)?.Format(Strings) ?? Strings["Signature_CheckFailed"];
    private static string FormatCatalog(SignatureResult trust) => trust.Status == SignatureStatus.NoEmbeddedSignature
        ? Strings["CatalogUnsigned"] : FormatTrust(trust);
    public string Signature => FormatTrust(File.Embedded);
    public string Sha256 => File.Hashes.GetValueOrDefault(ChecksumAlgorithm.SHA256) ?? "";
    public string Catalog => File.Catalogs switch
    {
        null => Strings["Signature_NotChecked"],
        { Matches.Count: > 0 } found => string.Join(" · ", found.Matches.Select(m => FormatCatalog(m.Trust)).Distinct()),
        { IndexComplete: false } or { Issues.Count: > 0 } => Strings["Incomplete"],
        _ => Strings["NoCatalog"]
    };

    public string Details
    {
        get
        {
            var lines = new List<string> { File.Path, $"{Strings["Folder"]} · {File.Root.Path} ({File.Root.Priority})",
                $"{Streams} · {Strings["Variant"]} {File.Variant}", $"{Strings["Embedded"]} · {Signature}" };
            if (File.IsHardLink) lines.Add(Strings["HardLink"]);
            if (File.Diagnostic != null) lines.Add(File.Diagnostic);
            if (RecycleError != null) lines.Add(RecycleError);
            if (File.Embedded.Diagnostic != null) lines.Add(File.Embedded.Diagnostic);
            foreach (var sig in File.Embedded.Signatures)
            {
                lines.Add($"{sig.Index} · {Strings["Signature_" + sig.Status]} · {sig.Certificate?.Publisher} · {sig.Certificate?.ThumbprintSha256}");
            }

            lines.Add($"{Strings["Catalog"]} · {Catalog}");
            if (File.Catalogs is { } lookup)
            {
                if (!lookup.IndexComplete) lines.Add(Strings["Incomplete"]);
                lines.AddRange(lookup.Issues);
                foreach (var match in lookup.Matches)
                {
                    lines.Add($"{Strings[match.Registered ? "Registered" : "Loose"]} · {match.Path}");
                    lines.Add($"{match.Member} · {FormatCatalog(match.Trust)} · {match.Trust.Certificate?.Publisher} · {match.Trust.NativeStatusCode:X8}");
                }
            }

            lines.AddRange(File.Hashes.Select(pair => $"{pair.Key}  {pair.Value}"));
            return string.Join(Environment.NewLine, lines);
        }
    }
}

public sealed class ResultsViewModel : ObservableObject
{
    private List<ResultRow> _rows = [];
    private ListCollectionView _view;
    private string _text = "";
    private int _kind;
    private SignatureStatus? _signature;
    private bool _exceptValid;

    public ResultsViewModel() => _view = BuildView();
    public ListCollectionView View { get => _view; private set => SetProperty(ref _view, value); }
    public IReadOnlyList<ResultRow> Rows => _rows;
    public int MarkedCount { get; private set; }

    public void Replace(IEnumerable<FoundFile> files) => Install(files.Select(f => new ResultRow(f)).ToList());

    public void Relocalize() => Install(_rows.Select(r => new ResultRow(r.File) { Marked = r.Marked, RecycleError = r.RecycleError }).ToList());

    public void RemoveMoved(IReadOnlyList<RecycleResult> result)
    {
        var moved = result.Where(r => r.Moved).Select(r => r.File).ToHashSet();
        var failed = result.Where(r => !r.Moved).ToDictionary(r => r.File, r => r.Reason);
        Install(_rows.Where(r => !moved.Contains(r.File)).Select(r => new ResultRow(r.File)
        {
            Marked = r.Marked,
            RecycleError = failed.GetValueOrDefault(r.File) ?? r.RecycleError
        }).ToList());
    }

    private void Install(List<ResultRow> rows)
    {
        foreach (var row in _rows) row.PropertyChanged -= MarkChanged;
        _rows = rows;
        var counts = rows.CountBy(r => r.File.Group).ToDictionary();
        foreach (var row in rows) row.CurrentGroup = counts[row.File.Group] > 1 ? row.File.Group : 0;
        foreach (var row in rows) row.PropertyChanged += MarkChanged;
        MarkedCount = rows.Count(r => r.Marked);
        OnPropertyChanged(nameof(MarkedCount));
        View = BuildView();
    }

    private void MarkChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ResultRow.Marked) || sender is not ResultRow row) return;
        MarkedCount += row.Marked ? 1 : -1;
        OnPropertyChanged(nameof(MarkedCount));
    }

    private ListCollectionView BuildView()
    {
        var view = new ListCollectionView(_rows);
        using (view.DeferRefresh())
        {
            view.Filter = value => Matches((ResultRow)value);
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ResultRow.GroupTitle)));
            view.SortDescriptions.Add(new(nameof(ResultRow.CurrentGroup), ListSortDirection.Descending));
            view.SortDescriptions.Add(new("File.Root.Priority", ListSortDirection.Descending));
            view.SortDescriptions.Add(new("File.Path", ListSortDirection.Ascending));
        }
        return view;
    }

    public void Filter(string text, int kind, SignatureStatus? signature, bool exceptValid)
    {
        _text = text.Trim();
        _kind = kind;
        _signature = signature;
        _exceptValid = exceptValid;
        View.Refresh();
    }

    private bool Matches(ResultRow row) =>
        (_kind != 1 || row.CurrentGroup != 0) && (_kind != 2 || row.CurrentGroup == 0)
        && (_kind != 3 || row.Diagnostic != null || row.File.Streams == StreamComparison.Unknown)
        && (_signature == null || row.File.Embedded.Status == _signature)
        && (!_exceptValid || row.File.Embedded.Status != SignatureStatus.Valid)
        && (_text.Length == 0 || row.File.Path.Contains(_text, StringComparison.OrdinalIgnoreCase)
            || row.File.Hashes.Values.Any(hash => hash.Contains(_text, StringComparison.OrdinalIgnoreCase))
            || row.File.Embedded.Certificate?.Publisher.Contains(_text, StringComparison.OrdinalIgnoreCase) == true
            || row.Diagnostic?.Contains(_text, StringComparison.OrdinalIgnoreCase) == true
            || row.File.Catalogs?.Matches.Any(m => m.Path.Contains(_text, StringComparison.OrdinalIgnoreCase)
                || m.Trust.Certificate?.Publisher.Contains(_text, StringComparison.OrdinalIgnoreCase) == true) == true);

    // Filter the proposed removals after choosing the keepers from the complete snapshot.
    // Whether WPF has realized or expanded a row is immaterial.
    public CleanupPlan Plan(bool filtered)
    {
        var scope = filtered ? _rows.Where(Matches).Select(r => r.File).ToHashSet() : null;
        return CleanupPlan.Create(_rows.Select(r => r.File), scope == null ? null : scope.Contains);
    }

    public IReadOnlyList<CleanupItem> Manual() => _rows.Where(r => r.Marked).Select(r => new CleanupItem(r.File, null)).ToArray();

    public int LastCopiesSelected() => _rows.GroupBy(r => (r.File.Variant, Single: r.File.Variant == 0 ? r.File.Path : ""))
        .Count(g => g.All(r => r.Marked));
}
