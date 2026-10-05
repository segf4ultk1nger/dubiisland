using System;
using System.Windows;
using System.Windows.Media;
using ClassIsland.Core.Models.Components;
using ClassIsland.Models.ComponentSettings;

namespace ClassIsland.Controls.Island.Components;

/// <summary>文本组件（等价 TextComponent.xaml）。</summary>
public sealed class TextIslandComponent : IslandComponentBase
{
    public TextIslandComponent(ComponentSettings component) : base(component)
    {
    }

    private TextComponentSettings Settings => IslandComponentFactory.GetSettings<TextComponentSettings>(Component);

    protected override Size MeasureContent(Size availableSize, IslandContext context)
    {
        var text = MakeText(Settings.TextContent ?? "", Settings.FontSize, Frozen(Settings.FontColor));
        return new Size(text.Width, Math.Max(text.Height, 40));
    }

    protected override void RenderContent(DrawingContext drawingContext, Rect slot, IslandContext context)
    {
        var text = MakeText(Settings.TextContent ?? "", Settings.FontSize, Frozen(Settings.FontColor));
        drawingContext.DrawText(text, new Point(slot.X, slot.Y + (slot.Height - text.Height) / 2));
    }
}
