using System.ComponentModel;
using System.Windows.Data;
using DuplicateFinder.Core.Models;

namespace DuplicateFinder.App.ViewModels;

public enum ResultKind { AllFiles, Duplicates, Singles, Diagnostics }

public sealed class ResultsViewModel : ObservableObject
{
    private List<ResultRow> _rows = [];
    private ListCollectionView _view;
    private string _searchText = "";
    private string _query = "";
    private ResultKind _kind;
    private SignatureFilter _signature;

    private HashFormat _hashFormat;

    public ResultsViewModel()
    {
        var strings = LocalizationService.Instance;
        ResultKinds = Enum.GetValues<ResultKind>().Select(kind => new FilterOption<ResultKind>(kind, kind.ToString(), strings)).ToArray();
        SignatureFilters = SignatureFilter.Options(strings);
        _view = BuildView();
    }

    public IReadOnlyList<FilterOption<ResultKind>> ResultKinds { get; }
    public IReadOnlyList<FilterOption<SignatureFilter>> SignatureFilters { get; }

    public string SearchText { get => _searchText; set => Filter(value, _kind, _signature); }
    public ResultKind SelectedKind { get => _kind; set => Filter(_searchText, value, _signature); }
    public SignatureFilter SelectedSignature { get => _signature; set => Filter(_searchText, _kind, value); }
    public RelayCommand ResetFiltersCommand => field ??= new(() => Filter("", ResultKind.AllFiles, SignatureFilter.All));

    public int VisibleCount => View.Count;
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
        if (_query.Length != 0) RefreshView();
    }

    public void Relocalize()
    {
        foreach (var option in ResultKinds) option.RefreshLanguage();
        foreach (var option in SignatureFilters) option.RefreshLanguage();
        foreach (var row in _rows) row.Relocalize();

        RefreshView();
    }

    public void RemoveMoved(IReadOnlyList<RecycleResult> result)
    {
        if (result.Count == 0) return;

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
        var counts = rows.Where(r => r.File.Group > 0).CountBy(r => r.File.Group).ToDictionary();
        foreach (var row in rows)
        {
            if (row.File.Group > 0) row.CurrentGroup = counts[row.File.Group] > 1 ? row.File.Group : 0;
            row.PropertyChanged += MarkChanged;
        }

        MarkedCount = rows.Count(r => r.Marked);
        OnPropertyChanged(nameof(MarkedCount));
        View = BuildView();
        OnPropertyChanged(nameof(VisibleCount));
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

    public void Filter(string text, ResultKind kind, SignatureFilter signature)
    {
        // Keep the user's spaces in the editor. Compare the trimmed query once per edit.
        var query = text.Trim();
        var refresh = query != _query || kind != _kind || signature != _signature;
        _query = query;

        SetProperty(ref _searchText, text, nameof(SearchText));
        SetProperty(ref _kind, kind, nameof(SelectedKind));
        SetProperty(ref _signature, signature, nameof(SelectedSignature));
        if (refresh) RefreshView();
    }

    private void RefreshView()
    {
        View.Refresh();
        OnPropertyChanged(nameof(VisibleCount));
    }

    private bool MatchesKind(ResultRow row) => _kind switch
    {
        ResultKind.AllFiles => true,
        ResultKind.Duplicates => row.CurrentGroup != 0,
        ResultKind.Singles => row.CurrentGroup == 0,
        ResultKind.Diagnostics => row.Diagnostic != null || row.File.Streams == StreamComparison.Unknown,
        _ => false
    };

    private bool Matches(ResultRow row) => MatchesKind(row)
        && _signature.Matches(row.File.Embedded.Status, row.File.Embedded.RevocationMode, row.CatalogResult)
        && (_query.Length == 0 || row.File.Path.Contains(_query, StringComparison.OrdinalIgnoreCase)
            || row.Signature.Contains(_query, StringComparison.CurrentCultureIgnoreCase)
            || row.MatchesHash(_query)
            || row.File.Embedded.Certificate?.Publisher.Contains(_query, StringComparison.OrdinalIgnoreCase) == true
            || row.Diagnostic?.Contains(_query, StringComparison.OrdinalIgnoreCase) == true
            || row.File.Catalogs?.Matches.Any(m => m.Path.Contains(_query, StringComparison.OrdinalIgnoreCase)
                || m.Trust.Certificate?.Publisher.Contains(_query, StringComparison.OrdinalIgnoreCase) == true) == true);

    public (CleanupPlan Filtered, CleanupPlan All) CreateCleanupPlans()
    {
        var all = CleanupPlan.Create(_rows.Select(row => row.File));
        if (all.Items.Count == 0 || View.Count == _rows.Count) return (all, all);

        // The collection view includes rows in collapsed groups. Filter only the removals
        // so each candidate keeps its reference copy from the complete scan.
        var visible = View.Cast<ResultRow>().Where(row => row.File.Variant > 0).Select(row => row.File).ToHashSet();
        var filtered = new CleanupPlan(all.Items.Where(item => visible.Contains(item.File)).ToArray());
        return (filtered, all);
    }

    public IReadOnlyList<CleanupItem> Manual() => _rows.Where(r => r.Marked).Select(r => new CleanupItem(r.File, null)).ToArray();

    public int LastCopiesSelected()
    {
        var singles = _rows.Count(r => r.File.Variant == 0 && r.Marked);
        var variants = _rows.Where(r => r.File.Variant > 0).GroupBy(r => r.File.Variant);
        return singles + variants.Count(group => group.All(r => r.Marked));
    }
}
