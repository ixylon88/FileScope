using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using FileScope.App.Services;
using FileScope.Core.Export;

namespace FileScope.App.ViewModels;

public sealed class OptionsViewModel : ObservableObject, IDisposable
{
    private readonly LocalizationService _strings;
    private readonly IPreferencesStore _store;
    private readonly IThemeService _themeService;
    private readonly IReadOnlyList<ColumnOption> _defaults;
    private readonly ObservableCollection<ColumnOption> _columns;
    private LanguageOption _selectedLanguage;
    private FilterOption<HashFormat> _selectedHashFormat;
    private FilterOption<CsvDelimiter> _selectedCsvDelimiter;
    private FilterOption<AppTheme> _selectedTheme;
    private ColumnOption? _selectedColumn;
    private string? _settingsErrorKey;
    private bool _updatingExportSelection;
    private bool _pathAutoWidth;
    private DialogValues? _beforeEdit;

    private sealed record DialogValues(LanguageOption Language, FilterOption<AppTheme> Theme,
        FilterOption<HashFormat> HashFormat, FilterOption<CsvDelimiter> CsvDelimiter, bool PathAutoWidth,
        string[] ColumnOrder, string[] ExportColumns, ColumnOption? SelectedColumn);

    public event EventHandler? HashFormatChanged;
    public event EventHandler? ColumnOrderChanged;
    public event EventHandler? ExportSelectionChanged;
    public ReadOnlyObservableCollection<ColumnOption> Columns { get; }
    public IReadOnlyList<LanguageOption> Languages => _strings.Languages;
    public IReadOnlyList<FilterOption<HashFormat>> HashFormats { get; }
    public IReadOnlyList<FilterOption<CsvDelimiter>> CsvDelimiters { get; }
    public IReadOnlyList<FilterOption<AppTheme>> Themes { get; }
    public string UiLanguage => _strings.Culture.IetfLanguageTag;
    public string SettingsError => _settingsErrorKey is null ? "" : _strings[_settingsErrorKey];
    public bool HasSettingsError => _settingsErrorKey is not null;
    public bool HasExportColumns => Columns.Any(column => column.IsExportSelected);
    public int WidthRevision { get; private set; }
    public WindowPreferences? WindowPreferences { get; private set; }
    public WindowPreferences? OptionsWindowPreferences { get; private set; }
    public WindowPreferences? AboutWindowPreferences { get; private set; }
    public WindowPreferences? ShortcutsWindowPreferences { get; private set; }
    public AnalysisPreferences AnalysisPreferences { get; private set; }
    public RelayCommand MoveUpCommand { get; }
    public RelayCommand MoveDownCommand { get; }
    public RelayCommand MoveFirstCommand { get; }
    public RelayCommand MoveLastCommand { get; }
    public RelayCommand ResetOrderCommand { get; }
    public RelayCommand FitColumnsCommand { get; }
    public RelayCommand SelectAllExportColumnsCommand { get; }
    public RelayCommand ClearExportColumnsCommand { get; }

    public OptionsViewModel(LocalizationService strings, IPreferencesStore store, IThemeService themes)
    {
        _strings = strings;
        _store = store;
        var saved = new UserPreferences();
        try { saved = store.Load(); }
        catch (Exception ex) when (IsSettingsFailure(ex)) { _settingsErrorKey = "SettingsLoadError"; }
        _pathAutoWidth = saved.PathAutoWidth;
        WindowPreferences = saved.Window;
        OptionsWindowPreferences = saved.OptionsWindow;
        AboutWindowPreferences = saved.AboutWindow;
        ShortcutsWindowPreferences = saved.ShortcutsWindow;
        AnalysisPreferences = saved.Analysis ?? new AnalysisPreferences();
        _selectedLanguage = Languages.FirstOrDefault(language => language.CultureName == saved.Language)
            ?? Languages.First(language => language.CultureName == strings.Culture.Name);
        strings.SetCulture(_selectedLanguage.CultureName);
        HashFormats = Enum.GetValues<HashFormat>()
            .Select(format => new FilterOption<HashFormat>(format, "HashFormat_" + format, strings)).ToArray();
        var format = Enum.TryParse<HashFormat>(saved.HashFormat, out var parsed) && Enum.IsDefined(parsed)
            ? parsed : HashFormat.HexUpper;
        _selectedHashFormat = HashFormats.Single(option => option.Value == format);
        CsvDelimiters = Enum.GetValues<CsvDelimiter>()
            .Select(delimiter => new FilterOption<CsvDelimiter>(delimiter, "CsvDelimiter_" + delimiter, strings)).ToArray();
        var delimiter = Enum.TryParse<CsvDelimiter>(saved.CsvDelimiter, out var parsedDelimiter)
            && Enum.IsDefined(parsedDelimiter) ? parsedDelimiter : CsvDelimiter.Semicolon;
        _selectedCsvDelimiter = CsvDelimiters.Single(option => option.Value == delimiter);
        _defaults = ColumnOption.CreateDefaults(strings);
        _columns = new ObservableCollection<ColumnOption>(NormalizeOrder(saved.ColumnOrder));
        Columns = new ReadOnlyObservableCollection<ColumnOption>(_columns);
        // null means an older settings file; [] deliberately means no columns.
        var exported = saved.ExportColumns?.ToHashSet(StringComparer.Ordinal);
        foreach (var column in Columns)
        {
            column.IsExportSelected = exported is null || exported.Contains(column.Id);
            column.PropertyChanged += OnColumnPropertyChanged;
        }
        _selectedColumn = Columns[0];
        MoveUpCommand = new RelayCommand(() => MoveSelected(SelectedIndex - 1), () => SelectedIndex > 0);
        MoveDownCommand = new RelayCommand(() => MoveSelected(SelectedIndex + 1),
            () => SelectedIndex >= 0 && SelectedIndex < Columns.Count - 1);
        MoveFirstCommand = new RelayCommand(() => MoveSelected(0), () => SelectedIndex > 0);
        MoveLastCommand = new RelayCommand(() => MoveSelected(Columns.Count - 1),
            () => SelectedIndex >= 0 && SelectedIndex < Columns.Count - 1);
        ResetOrderCommand = new RelayCommand(() => SetColumnOrder(_defaults.Select(column => column.Id)));
        FitColumnsCommand = new RelayCommand(RequestColumnSizing);
        SelectAllExportColumnsCommand = new RelayCommand(() => SetExportSelection(_ => true));
        ClearExportColumnsCommand = new RelayCommand(() => SetExportSelection(_ => false));
        strings.CultureChanged += OnCultureChanged;
        _themeService = themes;
        Themes = Array.AsReadOnly(new[]
        {
            new FilterOption<AppTheme>(AppTheme.Light, "Theme_Light", strings),
            new FilterOption<AppTheme>(AppTheme.Dark, "Theme_Dark", strings)
        });
        var savedTheme = Enum.TryParse<AppTheme>(saved.Theme, out var value) && Enum.IsDefined(value)
            ? value : themes.CurrentTheme;
        _selectedTheme = Themes.Single(theme => theme.Value == savedTheme);
        _themeService.Apply(_selectedTheme.Value);
    }

    // The modal dialog previews the same model used by the table. Keep a value snapshot, including
    // copies of both column lists, so the views can keep their bindings throughout a rollback.
    internal void BeginEdit()
    {
        if (_beforeEdit != null) throw new InvalidOperationException("An options dialog is already open.");
        _beforeEdit = CaptureDialogValues();
    }

    internal bool TryCommitEdit(WindowPreferences placement)
    {
        if (_beforeEdit == null) throw new InvalidOperationException("No options dialog is open.");
        if (!TryWrite(CaptureDialogValues(), placement)) return false;

        OptionsWindowPreferences = placement;
        _beforeEdit = null;
        return true;
    }

    internal void CancelEdit(WindowPreferences placement)
    {
        var original = _beforeEdit ?? throw new InvalidOperationException("No options dialog is open.");
        SelectedTheme = original.Theme;
        SelectedLanguage = original.Language;
        SelectedHashFormat = original.HashFormat;
        SelectedCsvDelimiter = original.CsvDelimiter;
        PathAutoWidth = original.PathAutoWidth;

        SetColumnOrder(original.ColumnOrder);
        var exported = original.ExportColumns.ToHashSet(StringComparer.Ordinal);
        SetExportSelection(column => exported.Contains(column.Id));
        SelectedColumn = original.SelectedColumn;

        _beforeEdit = null;
        if (OptionsWindowPreferences != placement || HasSettingsError) SaveOptionsWindowPreferences(placement);
    }

    public bool PathAutoWidth
    {
        get => _pathAutoWidth;
        set { if (SetProperty(ref _pathAutoWidth, value)) Save(); }
    }

    public LanguageOption SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (value is null || !Languages.Contains(value) || !SetProperty(ref _selectedLanguage, value)) return;
            _strings.SetCulture(value.CultureName);
            Save();
        }
    }
    public FilterOption<HashFormat> SelectedHashFormat
    {
        get => _selectedHashFormat;
        set
        {
            if (value is null || !HashFormats.Contains(value) || !SetProperty(ref _selectedHashFormat, value)) return;
            HashFormatChanged?.Invoke(this, EventArgs.Empty);
            RequestColumnSizing();
            Save();
        }
    }
    public FilterOption<CsvDelimiter> SelectedCsvDelimiter
    {
        get => _selectedCsvDelimiter;
        set
        {
            if (value is null || !CsvDelimiters.Contains(value) || !SetProperty(ref _selectedCsvDelimiter, value)) return;
            Save();
        }
    }
    public FilterOption<AppTheme> SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (value is null || !Themes.Contains(value) || value == _selectedTheme) return;
            _themeService.Apply(value.Value);
            SetProperty(ref _selectedTheme, value);
            Save();
        }
    }
    public ColumnOption? SelectedColumn
    {
        get => _selectedColumn;
        set
        {
            if (SetProperty(ref _selectedColumn, value)) NotifyMoveCommands();
        }
    }
    private int SelectedIndex => SelectedColumn is null ? -1 : _columns.IndexOf(SelectedColumn);

    public void SetColumnOrder(IEnumerable<string>? ids)
    {
        var ordered = NormalizeOrder(ids).ToArray();
        if (_columns.SequenceEqual(ordered)) return;
        var selected = SelectedColumn;
        for (var index = 0; index < ordered.Length; index++)
        {
            var oldIndex = _columns.IndexOf(ordered[index]);
            if (oldIndex != index) _columns.Move(oldIndex, index);
        }
        SelectedColumn = selected;
        NotifyMoveCommands();
        // Notify the view once, after the entire permutation is valid.
        ColumnOrderChanged?.Invoke(this, EventArgs.Empty);
        Save();
    }
    private IEnumerable<ColumnOption> NormalizeOrder(IEnumerable<string>? ids)
    {
        var available = _defaults.ToDictionary(column => column.Id, StringComparer.Ordinal);
        foreach (var id in ids ?? [])
            if (id is not null && available.Remove(id, out var column)) yield return column;
        foreach (var column in _defaults)
            if (available.ContainsKey(column.Id)) yield return column;
    }
    private void MoveSelected(int target)
    {
        if (SelectedIndex < 0 || target < 0 || target >= Columns.Count) return;
        var order = Columns.Select(column => column.Id).ToList();
        var id = order[SelectedIndex];
        order.RemoveAt(SelectedIndex);
        order.Insert(target, id);
        SetColumnOrder(order);
    }
    public void RequestColumnSizing()
    {
        WidthRevision = unchecked(WidthRevision + 1);
        OnPropertyChanged(nameof(WidthRevision));
    }
    private void NotifyMoveCommands()
    {
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
        MoveFirstCommand.NotifyCanExecuteChanged();
        MoveLastCommand.NotifyCanExecuteChanged();
    }
    public void SaveWindowPreferences(WindowPreferences window) =>
        SaveWindowPreferences(window, value => WindowPreferences = value);

    public void SaveOptionsWindowPreferences(WindowPreferences window) =>
        SaveWindowPreferences(window, value => OptionsWindowPreferences = value);

    public void SaveAboutWindowPreferences(WindowPreferences window) =>
        SaveWindowPreferences(window, value => AboutWindowPreferences = value);

    public void SaveShortcutsWindowPreferences(WindowPreferences window) =>
        SaveWindowPreferences(window, value => ShortcutsWindowPreferences = value);

    private void SaveWindowPreferences(WindowPreferences window, Action<WindowPreferences> assign)
    {
        if (!double.IsFinite(window.Width) || !double.IsFinite(window.Height)
            || window.Width <= 0 || window.Height <= 0)
        {
            return;
        }

        assign(window);
        SaveIndependentSettings();
    }

    public void SaveAnalysisPreferences(AnalysisPreferences analysis)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        if (AnalysisPreferences == analysis) return;
        AnalysisPreferences = analysis;
        SaveIndependentSettings();
    }

    private void SetExportSelection(Func<ColumnOption, bool> selected)
    {
        _updatingExportSelection = true;
        var changed = false;
        try
        {
            foreach (var column in Columns)
            {
                var value = selected(column);
                if (column.IsExportSelected == value) continue;
                column.IsExportSelected = value;
                changed = true;
            }
        }
        finally { _updatingExportSelection = false; }
        if (changed) NotifyExportSelection();
    }

    private void OnColumnPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (!_updatingExportSelection && args.PropertyName == nameof(ColumnOption.IsExportSelected))
            NotifyExportSelection();
    }

    private void NotifyExportSelection()
    {
        OnPropertyChanged(nameof(HasExportColumns));
        ExportSelectionChanged?.Invoke(this, EventArgs.Empty);
        Save();
    }

    private DialogValues CaptureDialogValues() => new(SelectedLanguage, SelectedTheme, SelectedHashFormat,
        SelectedCsvDelimiter, PathAutoWidth, Columns.Select(column => column.Id).ToArray(),
        Columns.Where(column => column.IsExportSelected).Select(column => column.Id).ToArray(), SelectedColumn);

    private void Save()
    {
        if (_beforeEdit == null) TryWrite(CaptureDialogValues(), OptionsWindowPreferences);
    }

    // A write originating outside the dialog must never include its unfinished edits.
    private void SaveIndependentSettings() => TryWrite(_beforeEdit ?? CaptureDialogValues(), OptionsWindowPreferences);

    private bool TryWrite(DialogValues values, WindowPreferences? placement)
    {
        try
        {
            _store.Save(new UserPreferences
            {
                Theme = values.Theme.Value.ToString(),
                Language = values.Language.CultureName,
                HashFormat = values.HashFormat.Value.ToString(),
                ColumnOrder = values.ColumnOrder,
                PathAutoWidth = values.PathAutoWidth,
                ExportColumns = values.ExportColumns,
                CsvDelimiter = values.CsvDelimiter.Value.ToString(),
                Window = WindowPreferences,
                OptionsWindow = placement,
                AboutWindow = AboutWindowPreferences,
                ShortcutsWindow = ShortcutsWindowPreferences,
                Analysis = AnalysisPreferences
            });
            _settingsErrorKey = null;
        }
        catch (Exception ex) when (IsSettingsFailure(ex)) { _settingsErrorKey = "SettingsSaveError"; }

        OnPropertyChanged(nameof(SettingsError));
        OnPropertyChanged(nameof(HasSettingsError));
        return !HasSettingsError;
    }

    private static bool IsSettingsFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or JsonException or System.Security.SecurityException;
    private void OnCultureChanged(object? sender, EventArgs args)
    {
        _selectedLanguage = Languages.Single(language => language.CultureName == _strings.Culture.Name);
        OnPropertyChanged(nameof(SelectedLanguage));
        foreach (var column in Columns) column.RefreshLanguage();
        foreach (var option in HashFormats) option.RefreshLanguage();
        foreach (var option in CsvDelimiters) option.RefreshLanguage();
        foreach (var option in Themes) option.RefreshLanguage();
        OnPropertyChanged(nameof(SettingsError));
        OnPropertyChanged(nameof(UiLanguage));
        RequestColumnSizing();
    }
    public void Dispose()
    {
        _strings.CultureChanged -= OnCultureChanged;
        foreach (var column in Columns) column.PropertyChanged -= OnColumnPropertyChanged;
    }
}
