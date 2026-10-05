using System;
using System.Windows;
using System.Windows.Media;
using ClassIsland.Core.Models.Components;
using ClassIsland.Models.ComponentSettings;

namespace ClassIsland.Controls.Island.Components;

/// <summary>时钟组件（等价 ClockComponent.xaml）。</summary>
public sealed class ClockIslandComponent : IslandComponentBase
{
    public ClockIslandComponent(ComponentSettings component) : base(component)
    {
    }

    private ClockComponentSettings Settings => IslandComponentFactory.GetSettings<ClockComponentSettings>(Component);

    private DateTime Now => Settings.ShowRealTime
        ? DateTime.Now
        : Context.ExactTimeService.GetCurrentLocalDateTime();

    private string Format => Settings.ShowSeconds ? "HH:mm:ss" : "HH:mm";

    protected override Size MeasureContent(Size availableSize, IslandContext context)
    {
        var text = MakeText(Now.ToString(Format), EmphasizedFontSize, ForegroundBrush);
        return new Size(text.Width, Math.Max(text.Height, 40));
    }

    protected override void RenderContent(DrawingContext drawingContext, Rect slot, IslandContext context)
    {
        var now = Now;
        // FlashTimeSeparator 且不显示秒时，秒的奇偶决定是否显示整串时间（闪烁效果）。
        var separatorShowing = !Settings.FlashTimeSeparator || Settings.ShowSeconds || now.Second % 2 == 1;
        if (!separatorShowing)
            return;

        var text = MakeText(now.ToString(Format), EmphasizedFontSize, ForegroundBrush);
        drawingContext.DrawText(text, new Point(slot.X + (slot.Width - text.Width) / 2,
            slot.Y + (slot.Height - text.Height) / 2));
    }
}
