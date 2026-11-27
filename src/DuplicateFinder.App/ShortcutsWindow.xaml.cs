using System.Windows;

namespace DuplicateFinder.App;

public partial class ShortcutsWindow : Window
{
    public RelayCommand CloseCommand { get; }

    public ShortcutsWindow()
    {
        CloseCommand = new(Close);
        InitializeComponent();
    }
}
