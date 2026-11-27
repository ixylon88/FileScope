using System.Windows;

namespace FileScope.App.Infrastructure;

// DataGridColumn is outside the visual tree and does not inherit DataContext.
// A Freezable in the view's resources supplies that inheritance context.
public sealed class BindingProxy : Freezable
{
    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
        nameof(Data), typeof(object), typeof(BindingProxy));

    public object? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    protected override Freezable CreateInstanceCore() => new BindingProxy();
}
