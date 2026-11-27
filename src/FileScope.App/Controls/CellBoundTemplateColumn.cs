using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace FileScope.App.Controls;

/// <summary>
/// Keeps a template cell's content bound to its owning cell, including while the
/// presenter is detached from the visual tree during creation or recycling.
/// </summary>
public sealed class CellBoundTemplateColumn : DataGridTemplateColumn
{
    protected override FrameworkElement GenerateElement(DataGridCell cell, object dataItem) =>
        BindToCell(base.GenerateElement(cell, dataItem), cell);

    protected override FrameworkElement GenerateEditingElement(DataGridCell cell, object dataItem) =>
        BindToCell(base.GenerateEditingElement(cell, dataItem), cell);

    private static FrameworkElement BindToCell(FrameworkElement element, DataGridCell cell)
    {
        // WPF's default Content={Binding} needs the presenter's parent to find
        // the row. Give it the cell explicitly before any deferred binding work
        // runs. Bind to DataContext, not dataItem: a recycled cell changes rows.
        // Base generation still selects templates; DataGridCell still disconnects
        // discarded presenters by replacing Content with its disconnected item.
        if (element is ContentPresenter presenter)
        {
            presenter.SetBinding(ContentPresenter.ContentProperty,
                new Binding(nameof(FrameworkElement.DataContext)) { Source = cell, Mode = BindingMode.OneWay });
        }
        return element;
    }
}
