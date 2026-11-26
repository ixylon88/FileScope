using System.Windows;

namespace DuplicateFinder.App;

public partial class ShortcutsWindow : Window
{
    public ShortcutsWindow() => InitializeComponent();

    private void CloseShortcuts(object sender, RoutedEventArgs e) => Close();
}
