using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using DuplicateFinder.App.Presentation;
using DuplicateFinder.App.Services;
using DuplicateFinder.Core.Models;
using FileScope.Core.Models;
using Microsoft.Win32;

namespace DuplicateFinder.App;

public partial class OptionsWindow : Window
{
    public sealed class RootEditor(string path, string priority)
    {
        public string Path { get; } = path;
        public string Priority { get; set; } = priority;
    }

    private readonly ObservableCollection<RootEditor> _roots;
    private static LocalizationService Strings => LocalizationService.Instance;
    private readonly WindowPlacement _placement;
    private WindowPreferences? _openingPlacement;
    private CultureInfo _inputCulture = Strings.Culture;
    private string? _errorKey;
    private string? _errorDetail;

    internal event Action<Preferences>? PreviewChanged;
    internal event Action<Preferences>? Saving;
    internal WindowPreferences? ChangedPlacement { get; private set; }
    public Preferences Result { get; private set; }

    public RelayCommand AddFoldersCommand { get; }
    public RelayCommand RemoveFoldersCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand CloseCommand { get; }

    public OptionsWindow(Preferences preferences)
    {
        AddFoldersCommand = new(AddFolders);
        RemoveFoldersCommand = new(RemoveFolders, () => RootsGrid?.SelectedItems.Count > 0);
        SaveCommand = new(() => DialogResult = true);
        CloseCommand = new(Close);

        InitializeComponent();
        Result = preferences;
        _placement = new WindowPlacement(this, preferences.OptionsWindow ?? new WindowPreferences(Width: 730, Height: 800, Maximized: false));
        Loaded += (_, _) => _openingPlacement = _placement.Capture();
        _roots = new(preferences.Roots.Select(r => new RootEditor(r.Path, r.Priority.ToString(Strings.Culture))));
        RootsGrid.ItemsSource = _roots;
        RootsGrid.SelectionChanged += (_, _) => RemoveFoldersCommand.NotifyCanExecuteChanged();

        MinimumUnit.ItemsSource = MaximumUnit.ItemsSource = FileSizeInput.Units;
        MinimumUnit.SelectedValue = preferences.MinimumSizeUnit;
        MaximumUnit.SelectedValue = preferences.MaximumSizeUnit;
        MinimumSize.Text = FileSizeInput.Format(preferences.SizeRange.MinimumBytes, preferences.MinimumSizeUnit, Strings.Culture);
        MaximumSize.Text = FileSizeInput.Format(preferences.SizeRange.MaximumBytes, preferences.MaximumSizeUnit, Strings.Culture);

        foreach (var hash in Enum.GetValues<ChecksumAlgorithm>())
        {
            HashOptions.Children.Add(new CheckBox
            {
                Content = hash.ToString(),
                Tag = hash,
                IsEnabled = hash != ChecksumAlgorithm.SHA256,
                IsChecked = hash == ChecksumAlgorithm.SHA256 || preferences.Hashes.Contains(hash),
                Margin = new Thickness(0, 0, 14, 0)
            });
        }

        Embedded.IsChecked = preferences.Embedded;
        Catalogs.IsChecked = preferences.Catalogs;
        Online.IsChecked = preferences.Online;
        AutoPathWidth.IsChecked = preferences.PathAutoWidth;

        DisplayOptions.LanguageSelector.ItemsSource = Strings.Languages;
        DisplayOptions.LanguageSelector.SelectedValue = preferences.Language;

        DisplayOptions.ThemeSelector.ItemsSource = Enum.GetValues<AppTheme>()
            .Select(theme => new FilterOption<AppTheme>(theme, "Theme_" + theme, Strings)).ToArray();
        DisplayOptions.ThemeSelector.SelectedValue = preferences.Theme;

        DisplayOptions.HashFormatSelector.ItemsSource = Enum.GetValues<HashFormat>()
            .Select(format => new FilterOption<HashFormat>(format, "HashFormat_" + format, Strings)).ToArray();
        DisplayOptions.HashFormatSelector.SelectedValue = preferences.HashFormat;

        DisplayOptions.LanguageSelector.SelectionChanged += Preview;
        DisplayOptions.ThemeSelector.SelectionChanged += Preview;
        DisplayOptions.HashFormatSelector.SelectionChanged += Preview;
        AutoPathWidth.Checked += Preview;
        AutoPathWidth.Unchecked += Preview;
        Strings.CultureChanged += UpdateLabels;
        Closed += (_, _) => Strings.CultureChanged -= UpdateLabels;
    }

    private Preferences DisplayChoices() => Result with
    {
        Language = (string)DisplayOptions.LanguageSelector.SelectedValue,
        Theme = (AppTheme)DisplayOptions.ThemeSelector.SelectedValue,
        HashFormat = (HashFormat)DisplayOptions.HashFormatSelector.SelectedValue,
        PathAutoWidth = AutoPathWidth.IsChecked == true
    };

    private void Preview(object sender, RoutedEventArgs e) => PreviewChanged?.Invoke(DisplayChoices());

    private void UpdateLabels(object? sender, EventArgs e)
    {
        ReformatSize(MinimumSize, (FileSizeUnit)MinimumUnit.SelectedValue);
        ReformatSize(MaximumSize, (FileSizeUnit)MaximumUnit.SelectedValue);
        _inputCulture = Strings.Culture;

        foreach (var theme in DisplayOptions.ThemeSelector.Items.Cast<FilterOption<AppTheme>>()) theme.RefreshLanguage();
        foreach (var format in DisplayOptions.HashFormatSelector.Items.Cast<FilterOption<HashFormat>>()) format.RefreshLanguage();
        UpdateErrorText();
    }

    private void ReformatSize(TextBox field, FileSizeUnit unit)
    {
        if (FileSizeInput.TryParse(field.Text, unit, _inputCulture, out var bytes))
            field.Text = FileSizeInput.Format(bytes, unit, Strings.Culture);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel) return;

        var placement = _placement.Capture();
        if (DialogResult == true)
            e.Cancel = !TrySave(placement);
        else if (placement != _openingPlacement)
            ChangedPlacement = placement;
    }

    private void AddFolders()
    {
        var picker = new OpenFolderDialog { Multiselect = true, Title = Strings["Add"] };
        if (picker.ShowDialog(this) != true) return;
        foreach (var path in picker.FolderNames)
        {
            if (!_roots.Any(r => string.Equals(path, r.Path, StringComparison.Ordinal)))
            {
                _roots.Add(new(path, "0"));
            }
        }
    }

    private void RemoveFolders()
    {
        foreach (var root in RootsGrid.SelectedItems.Cast<RootEditor>().ToArray()) _roots.Remove(root);
    }

    private bool TrySave(WindowPreferences placement)
    {
        RootsGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        RootsGrid.CommitEdit(DataGridEditingUnit.Row, true);
        var roots = new List<SearchRoot>();
        foreach (var entry in _roots)
        {
            if (!int.TryParse(entry.Priority, NumberStyles.Integer, Strings.Culture, out var priority))
            {
                ShowInputError("BadPriority", RootsGrid);
                return false;
            }
            roots.Add(new(entry.Path, priority));
        }

        var minUnit = (FileSizeUnit)MinimumUnit.SelectedValue;
        var maxUnit = (FileSizeUnit)MaximumUnit.SelectedValue;
        if (!FileSizeInput.TryParse(MinimumSize.Text, minUnit, Strings.Culture, out var min))
        {
            ShowInputError("BadSize", MinimumSize);
            return false;
        }
        if (!FileSizeInput.TryParse(MaximumSize.Text, maxUnit, Strings.Culture, out var max))
        {
            ShowInputError("BadSize", MaximumSize);
            return false;
        }
        if (min > max)
        {
            ShowInputError("ReversedSizes", MinimumSize);
            return false;
        }

        var next = DisplayChoices() with
        {
            OptionsWindow = placement,
            Roots = roots.ToArray(),
            Hashes = HashOptions.Children.OfType<CheckBox>().Where(h => h.IsChecked == true).Select(h => (ChecksumAlgorithm)h.Tag).ToArray(),
            SizeRange = new FileSizeRange(min, max),
            MinimumSizeUnit = minUnit,
            MaximumSizeUnit = maxUnit,
            Embedded = Embedded.IsChecked == true,
            Catalogs = Catalogs.IsChecked == true,
            Online = Online.IsChecked == true
        };

        try { Saving?.Invoke(next); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError("SettingsSaveError", ex.Message);
            return false;
        }

        Result = next;
        return true;
    }

    private void ShowInputError(string messageKey, UIElement field)
    {
        SearchTab.IsSelected = true;
        ShowError(messageKey);
        field.Focus();
    }

    private void ShowError(string key, string? detail = null)
    {
        _errorKey = key;
        _errorDetail = detail;
        UpdateErrorText();
    }

    private void UpdateErrorText()
    {
        if (_errorKey is { } key)
            DialogFooter.ErrorText = _errorDetail == null ? Strings[key] : $"{Strings[key]} {_errorDetail}";
    }
}
