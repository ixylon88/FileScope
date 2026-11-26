using System.Windows;
using DuplicateFinder.Core;

namespace DuplicateFinder.App;

public partial class ScopeWindow : Window
{
    public bool Filtered => VisibleFiles.IsChecked == true;
    public ScopeWindow(CleanupPlan filtered, CleanupPlan all)
    {
        InitializeComponent();
        var strings = LocalizationService.Instance;
        VisibleCount.Text = strings.Format("Counts", filtered.Items.Count, filtered.Groups);
        AllCount.Text = strings.Format("Counts", all.Items.Count, all.Groups);
        VisibleFiles.IsEnabled = filtered.Items.Count > 0;
        AllFiles.IsEnabled = all.Items.Count > 0;
        // Never expand the scope silently when the user's filter has zero candidates.
        VisibleFiles.IsChecked = VisibleFiles.IsEnabled;
        Confirm.IsEnabled = Filtered;
    }

    private void ChoiceChanged(object sender, RoutedEventArgs e) => Confirm.IsEnabled = true;
    private void Accept(object sender, RoutedEventArgs e) => DialogResult = true;
}
