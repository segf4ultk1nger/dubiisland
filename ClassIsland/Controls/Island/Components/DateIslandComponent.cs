using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using ClassIsland.Core.Models.Components;

namespace ClassIsland.Controls.Island.Components;

/// <summary>日期组件（等价 DateComponent.xaml：<c>{0:ddd MM/dd}</c>，zh-cn）。</summary>
public sealed class DateIslandComponent : IslandComponentBase
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("zh-cn");

    public DateIslandComponent(ComponentSettings component) : base(component)
    {
    }

    private string Text => Context.ExactTimeService.GetCurrentLocalDateTime().ToString("ddd MM/dd", Culture);

    protected override Size MeasureContent(Size availableSize, IslandContext context)
    {
        var text = MakeText(Text, BodyFontSize, ForegroundBrush);
        return new Size(text.Width, Math.Max(text.Height, 40));
    }

    protected override void RenderContent(DrawingContext drawingContext, Rect slot, IslandContext context)
    {
        var text = MakeText(Text, BodyFontSize, ForegroundBrush);
        drawingContext.DrawText(text, new Point(slot.X, slot.Y + (slot.Height - text.Height) / 2));
    }
}
