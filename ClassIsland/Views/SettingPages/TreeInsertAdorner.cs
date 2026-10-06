using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using ClassIsland.Core.Models.Components;
using GongSolutions.Wpf.DragDrop;

namespace ClassIsland.Views.SettingPages;

/// <summary>
/// 组件树拖拽时的放置指示：悬停项上/下 1/3 画插入线，中间 1/3 且目标是容器/行时画整行高亮。
/// </summary>
public sealed class TreeInsertAdorner : DropTargetAdorner
{
    private static readonly Pen DefaultPen = CreatePen();

    private static Pen CreatePen()
    {
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(0xE3, 0x00, 0x6F)), 2);
        pen.Freeze();
        return pen;
    }

    public TreeInsertAdorner(UIElement adornedElement, IDropInfo dropInfo) : base(adornedElement, dropInfo)
    {
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var bounds = AdornedElement.RenderSize;
        if (bounds.Width <= 0 || bounds.Height <= 0 || DropInfo?.VisualTarget is not { } target)
            return;

        var item = ResolveItem(target);
        if (item == null)
            return;

        var pen = Pen ?? DefaultPen;
        var height = item.ActualHeight;
        var itemTop = ToAncestor(item, AdornedElement);
        var fy = height <= 0 ? 0.5 : (DropInfo.DropPosition.Y - ToAncestor(item, target).Y) / height;

        if (fy < 0.33 || fy > 0.67 || !IsContainer(item))
        {
            var y = itemTop.Y + (fy < 0.5 ? 0 : height);
            drawingContext.DrawLine(pen, new Point(itemTop.X, y), new Point(bounds.Width - 1, y));
        }
        else
        {
            drawingContext.DrawRoundedRectangle(null, pen,
                new Rect(itemTop.X + 1, itemTop.Y + 1, bounds.Width - itemTop.X - 2, height - 2), 3, 3);
        }
    }

    private TreeViewItem? ResolveItem(UIElement target)
        => ComponentTreeHitTest.GetItem(target, DropInfo.DropPosition)
           ?? DropInfo.VisualTargetItem as TreeViewItem;

    private static Point ToAncestor(Visual visual, Visual ancestor)
    {
        try
        {
            return visual.TransformToAncestor(ancestor).Transform(new Point(0, 0));
        }
        catch
        {
            return new Point(0, 0);
        }
    }

    private static bool IsContainer(UIElement element)
        => (element as FrameworkElement)?.DataContext switch
        {
            MainWindowLineSettings => true,
            ComponentSettings component => component.Children != null,
            _ => false
        };
}

/// <summary>
/// 命中光标下真正的 <see cref="TreeViewItem"/>。不能依赖 <see cref="IDropInfo.VisualTargetItem"/>：
/// GongSolutions 在悬停展开项的底部 25% 时会把它改成第一个子项。位置必须用 <see cref="IDropInfo.DropPosition"/>
/// （来自拖拽事件），拖拽过程中 <see cref="System.Windows.Input.Mouse.GetPosition"/> 不可靠。
/// </summary>
internal static class ComponentTreeHitTest
{
    public static TreeViewItem? GetItem(UIElement? relativeTo, Point position)
    {
        if (relativeTo is not Visual visual)
            return null;
        var hit = VisualTreeHelper.HitTest(visual, position);
        DependencyObject? current = hit?.VisualHit;
        while (current != null && current is not TreeViewItem)
        {
            current = VisualTreeHelper.GetParent(current);
        }
        return current as TreeViewItem;
    }
}
