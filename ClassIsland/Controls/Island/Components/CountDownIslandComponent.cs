using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using ClassIsland.Core.Models.Components;
using ClassIsland.Models.ComponentSettings;

namespace ClassIsland.Controls.Island.Components;

/// <summary>倒计时日组件（等价 CountDownComponent.xaml）。</summary>
public sealed class CountDownIslandComponent : IslandComponentBase
{
    public CountDownIslandComponent(ComponentSettings component) : base(component)
    {
    }

    private CountDownComponentSettings Settings => IslandComponentFactory.GetSettings<CountDownComponentSettings>(Component);

    protected override Size MeasureContent(Size availableSize, IslandContext context)
    {
        var parts = BuildParts();
        var width = 0.0;
        var height = 0.0;
        foreach (var part in parts)
        {
            width += part.Width;
            height = Math.Max(height, part.Height);
        }

        return new Size(width, Math.Max(height, 40));
    }

    protected override void RenderContent(DrawingContext drawingContext, Rect slot, IslandContext context)
    {
        var parts = BuildParts();
        var x = slot.X;
        foreach (var part in parts)
        {
            drawingContext.DrawText(part, new Point(x, slot.Y + (slot.Height - part.Height) / 2));
            x += part.Width;
        }
    }

    private List<FormattedText> BuildParts()
    {
        var settings = Settings;
        var emphasizedBrush = Frozen(settings.FontColor);
        var connectorBrush = settings.IsConnectorColorEmphasized ? emphasizedBrush : ForegroundBrush;
        var days = Math.Max((settings.OverTime.Date - Context.ExactTimeService.GetCurrentLocalDateTime().Date).Days, 0);
        var result = new List<FormattedText>();

        if (!settings.IsCompactModeEnabled)
            result.Add(MakeText("距离", BodyFontSize, connectorBrush));
        result.Add(MakeText(settings.CountDownName, settings.FontSize, emphasizedBrush, FontWeights.Medium));
        if (!settings.IsCompactModeEnabled)
            result.Add(MakeText(settings.CountDownConnector, BodyFontSize, connectorBrush));
        result.Add(MakeText(days.ToString(), settings.FontSize, emphasizedBrush, FontWeights.Medium));
        result.Add(MakeText("天", settings.FontSize, connectorBrush, FontWeights.Medium));
        return result;
    }
}
