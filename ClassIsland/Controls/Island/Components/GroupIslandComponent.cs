using System;
using System.Windows;
using System.Windows.Media;
using ClassIsland.Core.Models.Components;
using ClassIsland.Models.ComponentSettings;

namespace ClassIsland.Controls.Island.Components;

/// <summary>分组组件（等价 GroupComponent.xaml）：把多个组件横向组合显示。</summary>
public sealed class GroupIslandComponent : ContainerIslandComponentBase
{
    public GroupIslandComponent(ComponentSettings component, IslandContext context) : base(component, context)
    {
    }

    protected override Size MeasureContent(Size availableSize, IslandContext context)
        => MeasureChildren(availableSize, context);

    protected override void RenderContent(DrawingContext drawingContext, Rect slot, IslandContext context)
        => RenderChildren(drawingContext, slot, context);
}
