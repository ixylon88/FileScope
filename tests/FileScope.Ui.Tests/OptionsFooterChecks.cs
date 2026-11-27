using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace FileScope.Ui.Tests;

internal sealed class OptionsFooterChecks(Window window)
{
    private readonly FrameworkElement _footer = (FrameworkElement)((OptionsFooterHost)window.FindName("DialogFooter")).Child;

    public Button Cancel => (Button)_footer.FindName("CancelButton");
    public Button Save => (Button)_footer.FindName("SaveButton");
    public TextBlock Error => (TextBlock)_footer.FindName("ErrorMessage");

    public void CheckLayout(LocalizationService strings)
    {
        var hint = (TextBlock)_footer.FindName("PreviewHint");
        Assert.AreEqual(strings["OptionsPreviewHint"], hint.Text);
        Assert.AreEqual(strings["Cancel"], Cancel.Content);
        Assert.AreEqual(strings["Save"], Save.Content);
        Assert.IsTrue(Cancel.IsCancel && Save.IsDefault);

        Assert.AreEqual(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(Error));
        var critical = (SolidColorBrush)Error.FindResource("SystemFillColorCriticalBrush");
        Assert.AreEqual(critical.Color, ((SolidColorBrush)Error.Foreground).Color);

        var root = (FrameworkElement)window.Content;
        Rect Bounds(FrameworkElement element) => element.TransformToAncestor(root).TransformBounds(new Rect(element.RenderSize));
        var cancel = Bounds(Cancel);
        var save = Bounds(Save);

        Assert.IsTrue(Bounds(hint).Right + 15 <= cancel.Left);
        Assert.AreEqual(8.0, save.Left - cancel.Right, 1.0);
        Assert.AreEqual(cancel.Bottom, save.Bottom, 1.0);
        Assert.AreEqual(root.ActualWidth, save.Right, 1.0);
        Assert.IsTrue(save.Bottom <= root.ActualHeight + 1);
        if (Error.IsVisible)
        {
            Assert.IsTrue(Bounds(hint).Bottom <= Bounds(Error).Top);
            Assert.IsTrue(Bounds(Error).Right + 15 <= cancel.Left);
        }
    }
}
