using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using DuplicateFinder.Core;
using FileScope.App.Localization;
using FileScope.App.Services;
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
    public Preferences Result { get; private set; }

    public OptionsWindow(Preferences preferences)
    {
        InitializeComponent();
        Result = preferences;
        _roots = new(preferences.Roots.Select(r => new RootEditor(r.Path, r.Priority.ToString(Strings.Culture))));
        RootsGrid.ItemsSource = _roots;

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
        Languages.ItemsSource = Strings.Languages;
        Languages.SelectedValue = preferences.Language;
        Themes.ItemsSource = new[] { Strings["Light"], Strings["Dark"] };
        Themes.SelectedIndex = (int)preferences.Theme;
    }

    private void AddFolders(object sender, RoutedEventArgs e)
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

    private void RemoveFolders(object sender, RoutedEventArgs e)
    {
        foreach (var root in RootsGrid.SelectedItems.Cast<RootEditor>().ToArray()) _roots.Remove(root);
    }

    private void Save(object sender, RoutedEventArgs e)
    {
        RootsGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        RootsGrid.CommitEdit(DataGridEditingUnit.Row, true);
        var roots = new List<SearchRoot>();
        foreach (var entry in _roots)
        {
            if (!int.TryParse(entry.Priority, NumberStyles.Integer, Strings.Culture, out var priority))
            {
                Validation.Text = Strings["BadPriority"];
                return;
            }
            roots.Add(new(entry.Path, priority));
        }

        Result = new Preferences
        {
            Roots = roots.ToArray(),
            Language = (string)Languages.SelectedValue,
            Theme = (AppTheme)Themes.SelectedIndex,
            Hashes = HashOptions.Children.OfType<CheckBox>().Where(h => h.IsChecked == true).Select(h => (ChecksumAlgorithm)h.Tag).ToArray(),
            Embedded = Embedded.IsChecked == true,
            Catalogs = Catalogs.IsChecked == true,
            Online = Online.IsChecked == true
        };
        DialogResult = true;
    }
}
