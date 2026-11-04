using System.Windows;
using FileScope.App.ViewModels;

namespace FileScope.App;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Closed += (_, _) => viewModel.Dispose();
    }

    private void OpenOptions(object sender, RoutedEventArgs args)
    {
        if (DataContext is MainViewModel viewModel)
            new OptionsWindow(viewModel.Options) { Owner = this }.ShowDialog();
    }
}
