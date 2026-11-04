using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace FileScope.App.Behaviors;

public static class DataGridScrollFix
{
    private static readonly IValueConverter WidthConverter = new NonNegativeWidthConverter();

    // The .NET 10 Fluent template binds its select-all button's Width directly
    // to CellsPanelHorizontalOffset. Layout/scroll rounding can make that offset
    // slightly negative, even with row headers hidden. Fix just this binding;
    // keep the native template, column sizing, frozen columns and virtualization.
    public static void Apply(DataGrid grid)
    {
        grid.ApplyTemplate();
        if (grid.Template?.FindName("DG_ScrollViewer", grid) is not ScrollViewer viewer) return;
        viewer.ApplyTemplate();
        foreach (var button in TemplateChildren(viewer, viewer).OfType<Button>())
        {
            if (button.Command != DataGrid.SelectAllCommand) continue;
            var binding = BindingOperations.GetBinding(button, FrameworkElement.WidthProperty);
            if (binding?.Path?.Path != nameof(DataGrid.CellsPanelHorizontalOffset)
                || binding.Converter is not null) continue;

            button.SetBinding(FrameworkElement.WidthProperty, new Binding(nameof(DataGrid.CellsPanelHorizontalOffset))
            {
                Source = grid,
                Mode = BindingMode.OneWay,
                Converter = WidthConverter
            });
        }
    }

    private static IEnumerable<FrameworkElement> TemplateChildren(DependencyObject parent, Control owner)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            // Only visit this ScrollViewer's template, never cells or user content.
            if (VisualTreeHelper.GetChild(parent, index) is not FrameworkElement child
                || !ReferenceEquals(child.TemplatedParent, owner)) continue;
            yield return child;
            foreach (var descendant in TemplateChildren(child, owner)) yield return descendant;
        }
    }

    private sealed class NonNegativeWidthConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is double width && double.IsFinite(width) ? Math.Max(0d, width) : 0d;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            Binding.DoNothing;
    }
}
