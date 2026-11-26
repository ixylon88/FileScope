using System.Windows;

namespace DuplicateFinder.App;

public partial class ReportWindow : Window
{
    private static LocalizationService Strings => LocalizationService.Instance;
    public ReportWindow(string title, string intro, IEnumerable<string> lines, bool confirm = false)
    {
        InitializeComponent();
        Title = title;
        Intro.Text = intro;
        Lines.ItemsSource = lines;
        if (confirm)
        {
            CloseButton.Content = Strings["Cancel"];
            Proceed.Visibility = Visibility.Visible;
        }
    }

    private void Accept(object sender, RoutedEventArgs e) => DialogResult = true;
}
