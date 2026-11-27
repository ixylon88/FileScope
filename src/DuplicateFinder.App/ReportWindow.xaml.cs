using System.Windows;

namespace DuplicateFinder.App;

public partial class ReportWindow : Window
{
    private static LocalizationService Strings => LocalizationService.Instance;
    public RelayCommand ConfirmCommand { get; }
    public RelayCommand CloseCommand { get; }

    public ReportWindow(string title, string intro, IEnumerable<string> lines, bool confirm = false)
    {
        ConfirmCommand = new(() => DialogResult = true, () => confirm);
        CloseCommand = new(Close);

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
}
