using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace ClassIsland.Controls;

/// <summary>
/// Combo box column whose items source is a binding, previously provided by the Material column.
/// </summary>
public class ItemsSourceDataGridComboBoxColumn : DataGridComboBoxColumn
{
    public BindingBase? ItemsSourceBinding { get; set; }

    protected override FrameworkElement GenerateElement(DataGridCell cell, object dataItem)
    {
        var element = base.GenerateElement(cell, dataItem);
        ApplyItemsSource(element);
        return element;
    }

    protected override FrameworkElement GenerateEditingElement(DataGridCell cell, object dataItem)
    {
        var element = base.GenerateEditingElement(cell, dataItem);
        ApplyItemsSource(element);
        return element;
    }

    private void ApplyItemsSource(FrameworkElement element)
    {
        if (ItemsSourceBinding != null)
        {
            element.SetBinding(ItemsControl.ItemsSourceProperty, ItemsSourceBinding);
        }
    }
}
