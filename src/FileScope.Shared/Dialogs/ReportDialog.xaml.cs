using System.Windows;
using System.Windows.Controls;
using FileScope.Shared.Infrastructure;
using FileScope.Shared.Localization;
using FileScope.Shared.Presentation;

namespace FileScope.Shared.Dialogs;

/// <summary>A modal report with selectable text or a virtualized list of entries.</summary>
public partial class ReportDialog : Window
{
    private readonly bool _canConfirm;

    public RelayCommand CloseCommand => field ??= new(Close);
    public RelayCommand ConfirmCommand => field ??= new(() => DialogResult = true, () => _canConfirm);

    private ReportDialog(string titleKey, string? confirmationKey, WindowSize? size)
    {
        _canConfirm = confirmationKey is not null;
        InitializeComponent();
        SetBinding(TitleProperty, LocExtension.CreateBinding(titleKey));

        if (confirmationKey is not null)
        {
            CloseButton.SetBinding(ContentControl.ContentProperty, LocExtension.CreateBinding("Cancel"));
            CloseButton.IsDefault = false;
            ConfirmButton.SetBinding(ContentControl.ContentProperty, LocExtension.CreateBinding(confirmationKey));
            ConfirmButton.Visibility = Visibility.Visible;
        }

        if (size is not null)
        {
            MinWidth = size.MinWidth;
            MinHeight = size.MinHeight;
        }
        WindowPlacement.FitToWorkArea(this, size?.Width, size?.Height);
    }

    public static ReportDialog ForText(string titleKey, Func<LocalizationService, string> summary,
        Func<LocalizationService, string> text, WindowSize? size = null)
    {
        var dialog = new ReportDialog(titleKey, null, size);
        dialog.ReportText.Visibility = Visibility.Visible;
        dialog.Localize(summary, strings => dialog.ReportText.Text = text(strings));
        return dialog;
    }

    public static ReportDialog ForLines(string titleKey, Func<LocalizationService, string> summary,
        Func<LocalizationService, IReadOnlyList<string>> lines, string? confirmationKey = null)
    {
        var dialog = new ReportDialog(titleKey, confirmationKey, null);
        dialog.Lines.Visibility = Visibility.Visible;
        dialog.Localize(summary, strings => dialog.Lines.ItemsSource = lines(strings));
        return dialog;
    }

    private void Localize(Func<LocalizationService, string> summary, Action<LocalizationService> showBody)
    {
        var strings = LocalizationService.Instance;

        void Refresh(object? sender, EventArgs args)
        {
            SummaryText.Text = summary(strings);
            showBody(strings);
        }

        Loaded += (_, _) =>
        {
            Refresh(this, EventArgs.Empty);
            strings.CultureChanged += Refresh;
        };
        Unloaded += (_, _) => strings.CultureChanged -= Refresh;
    }
}
