using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;

namespace FileScope.Shared;

/// <summary>Shows the preview hint, the current error and the two options dialog commands.</summary>
public partial class OptionsFooter : DockPanel
{
    public static readonly DependencyProperty CancelCommandProperty =
        DependencyProperty.Register(nameof(CancelCommand), typeof(ICommand), typeof(OptionsFooter));

    public static readonly DependencyProperty SaveCommandProperty =
        DependencyProperty.Register(nameof(SaveCommand), typeof(ICommand), typeof(OptionsFooter));

    public static readonly DependencyProperty ErrorTextProperty = DependencyProperty.Register(
        nameof(ErrorText), typeof(string), typeof(OptionsFooter),
        new PropertyMetadata(string.Empty, (element, args) => ((OptionsFooter)element).ShowError((string?)args.NewValue)));

    public ICommand? CancelCommand { get => (ICommand?)GetValue(CancelCommandProperty); set => SetValue(CancelCommandProperty, value); }
    public ICommand? SaveCommand { get => (ICommand?)GetValue(SaveCommandProperty); set => SetValue(SaveCommandProperty, value); }
    public string? ErrorText { get => (string?)GetValue(ErrorTextProperty); set => SetValue(ErrorTextProperty, value); }

    public OptionsFooter()
    {
        InitializeComponent();
        Loaded += (_, _) => AnnounceError();
    }

    private void ShowError(string? message)
    {
        ErrorMessage.Text = message ?? string.Empty;
        ErrorMessage.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
        AnnounceError();
    }

    private void AnnounceError()
    {
        if (IsLoaded && ErrorMessage.IsVisible && AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged))
            UIElementAutomationPeer.CreatePeerForElement(ErrorMessage)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
}
