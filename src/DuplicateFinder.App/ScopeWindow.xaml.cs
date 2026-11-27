using System.Windows;
using DuplicateFinder.Core.Models;

namespace DuplicateFinder.App;

public partial class ScopeWindow : Window
{
    public bool Filtered => VisibleFiles.IsChecked == true;
    public RelayCommand ConfirmCommand { get; }
    public RelayCommand CloseCommand { get; }

    public ScopeWindow(CleanupPlan filtered, CleanupPlan all)
    {
        CloseCommand = new(Close);
        ConfirmCommand = new(() => DialogResult = true, HasSelectedScope);

        InitializeComponent();
        var strings = LocalizationService.Instance;
        VisibleCount.Text = strings.Format("Counts", filtered.Items.Count, filtered.Groups);
        AllCount.Text = strings.Format("Counts", all.Items.Count, all.Groups);
        VisibleFiles.IsEnabled = filtered.Items.Count > 0;
        AllFiles.IsEnabled = all.Items.Count > 0;
        // Never expand the scope silently when the user's filter has zero candidates.
        VisibleFiles.IsChecked = VisibleFiles.IsEnabled;
        ConfirmCommand.NotifyCanExecuteChanged();
    }

    private bool HasSelectedScope() =>
        VisibleFiles is { IsChecked: true, IsEnabled: true } || AllFiles is { IsChecked: true, IsEnabled: true };

    private void ChoiceChanged(object sender, RoutedEventArgs e) => ConfirmCommand.NotifyCanExecuteChanged();
}
