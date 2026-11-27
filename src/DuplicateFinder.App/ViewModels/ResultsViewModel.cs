using System.ComponentModel;
using System.Windows.Data;
using DuplicateFinder.Core.Models;

namespace DuplicateFinder.App.ViewModels;

public sealed class ResultsViewModel : ObservableObject
{
    private List<ResultRow> _rows = [];
    private ListCollectionView _view;
    private string _text = "";
    private int _kind;
    private SignatureFilter _signature;

    private HashFormat _hashFormat;

    public ResultsViewModel() => _view = BuildView();
    public ListCollectionView View { get => _view; private set => SetProperty(ref _view, value); }
    public IReadOnlyList<ResultRow> Rows => _rows;
    public int MarkedCount { get; private set; }

    public bool IsBusy
    {
        get;
        internal set
        {
            if (SetProperty(ref field, value)) OnPropertyChanged(nameof(CanMark));
        }
    }

    public bool CanMark => !IsBusy;

    public void Replace(IEnumerable<FoundFile> files) => Install(files.Select(f => new ResultRow(f, _hashFormat)).ToList());

    public void SetHashFormat(HashFormat format)
    {
        if (_hashFormat == format) return;
        _hashFormat = format;
        foreach (var row in _rows) row.SetHashFormat(format);
        if (_text.Length != 0) View.Refresh();
    }

    public void Relocalize()
    {
        foreach (var row in _rows) row.Relocalize();
        View.Refresh();
    }

    public void RemoveMoved(IReadOnlyList<RecycleResult> result)
    {
        var moved = result.Where(r => r.Moved).Select(r => r.File).ToHashSet();
        var failed = result.Where(r => !r.Moved).ToDictionary(r => r.File, r => r.Reason);
        Install(_rows.Where(r => !moved.Contains(r.File)).Select(r => new ResultRow(r.File, _hashFormat)
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

    public void Filter(string text, int kind, SignatureFilter signature)
    {
        _text = text.Trim();
        _kind = kind;
        _signature = signature;
        View.Refresh();
    }

    private bool Matches(ResultRow row) =>
        (_kind != 1 || row.CurrentGroup != 0) && (_kind != 2 || row.CurrentGroup == 0)
        && (_kind != 3 || row.Diagnostic != null || row.File.Streams == StreamComparison.Unknown)
        && _signature.Matches(row.File.Embedded.Status, row.File.Embedded.RevocationMode)
        && (_text.Length == 0 || row.File.Path.Contains(_text, StringComparison.OrdinalIgnoreCase)
            || row.Signature.Contains(_text, StringComparison.CurrentCultureIgnoreCase)
            || row.MatchesHash(_text)
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
