using System.Windows;
using System.Windows.Media;

namespace FileScope.Ui.Tests;

internal static class VisualTreeQueries
{
    internal static IEnumerable<T> VisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) yield return match;

            foreach (var descendant in VisualChildren<T>(child)) yield return descendant;
        }
    }
}
