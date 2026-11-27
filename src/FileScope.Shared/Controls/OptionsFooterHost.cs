using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;

namespace FileScope.Shared;

/// <summary>Shows the preview hint, the current error and the two options dialog commands.</summary>
public sealed class OptionsFooterHost : Decorator
{
    private readonly TextBlock _errorMessage;

    public static readonly DependencyProperty CancelCommandProperty =
        DependencyProperty.Register(nameof(CancelCommand), typeof(ICommand), typeof(OptionsFooterHost));

    public static readonly DependencyProperty SaveCommandProperty =
        DependencyProperty.Register(nameof(SaveCommand), typeof(ICommand), typeof(OptionsFooterHost));

    public static readonly DependencyProperty ErrorTextProperty = DependencyProperty.Register(
        nameof(ErrorText), typeof(string), typeof(OptionsFooterHost),
        new PropertyMetadata(string.Empty, (element, args) => ((OptionsFooterHost)element).ShowError((string?)args.NewValue)));

    public ICommand? CancelCommand { get => (ICommand?)GetValue(CancelCommandProperty); set => SetValue(CancelCommandProperty, value); }
    public ICommand? SaveCommand { get => (ICommand?)GetValue(SaveCommandProperty); set => SetValue(SaveCommandProperty, value); }
    public string? ErrorText { get => (string?)GetValue(ErrorTextProperty); set => SetValue(ErrorTextProperty, value); }
    public LocalizationService Strings { get; } = LocalizationService.Instance;

    public OptionsFooterHost()
    {
        // Keep the standalone layout and its host distinct, even if an older designer-generated OptionsFooter still exists.
        var view = (DockPanel)Application.LoadComponent(new Uri("/FileScope.Shared;component/Controls/OptionsFooter.xaml", UriKind.Relative));
        _errorMessage = (TextBlock)view.FindName("ErrorMessage");
        view.DataContext = this;
        Child = view;

        Loaded += (_, _) => AnnounceError();
    }

    private void ShowError(string? message)
    {
        _errorMessage.Text = message ?? string.Empty;
        _errorMessage.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
        AnnounceError();
    }

    private void AnnounceError()
    {
        if (IsLoaded && _errorMessage.IsVisible && AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged))
            UIElementAutomationPeer.CreatePeerForElement(_errorMessage)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
}
