using System;
using System.Windows;
using System.Windows.Media;
using ClassIsland.Core.Models.Components;

namespace ClassIsland.Controls.Island.Components;

/// <summary>分割线组件（等价 SeparatorComponent.xaml）。</summary>
public sealed class SeparatorIslandComponent : IslandComponentBase
{
    public SeparatorIslandComponent(ComponentSettings component) : base(component)
    {
    }

    protected override Size MeasureContent(Size availableSize, IslandContext context) => new(0, 40);

    protected override void RenderContent(DrawingContext drawingContext, Rect slot, IslandContext context)
    {
        var baseColor = context.ForegroundColor;
        var brush = new SolidColorBrush(baseColor) { Opacity = 0.8 };
        brush.Freeze();
        var pen = new Pen(brush, 2);
        pen.Freeze();
        var x = slot.X + slot.Width / 2;
        drawingContext.DrawLine(pen,
            new Point(x, slot.Y + (slot.Height - 25) / 2),
            new Point(x, slot.Y + (slot.Height + 25) / 2));
    }
}
