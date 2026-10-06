using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using ClassIsland.Core.Models.Components;

namespace ClassIsland.Controls.Island;

/// <summary>
/// 容器组件基类：按 <see cref="ComponentSettings.Children"/> 递归构建子组件，子组件横向排布。
/// </summary>
public abstract class ContainerIslandComponentBase : IslandComponentBase
{
    private readonly IslandContext _creationContext;
    private readonly List<IIslandComponent> _children = new();

    protected ContainerIslandComponentBase(ComponentSettings component, IslandContext context) : base(component)
    {
        _creationContext = context;
        RebuildChildren();
    }

    protected IReadOnlyList<IIslandComponent> Children => _children;

    public override bool IsVisible => base.IsVisible && Component.Children is { Count: > 0 };

    /// <summary>把所有子组件测量到一行，返回总宽与最大高（均含子组件外边距）。</summary>
    protected Size MeasureChildren(Size availableSize, IslandContext context)
    {
        var width = 0.0;
        var height = 0.0;
        foreach (var child in _children.Where(c => c.IsVisible))
        {
            var size = child.Measure(availableSize, context);
            width += size.Width;
            height = System.Math.Max(height, size.Height);
        }

        return new Size(width, System.Math.Max(height, 40));
    }

    protected void RenderChildren(DrawingContext drawingContext, Rect slot, IslandContext context)
    {
        var x = slot.X;
        foreach (var child in _children.Where(c => c.IsVisible))
        {
            var width = child.Measure(new Size(double.PositiveInfinity, slot.Height), context).Width;
            child.Render(drawingContext, new Rect(x, slot.Y, width, slot.Height), context);
            x += width;
        }
    }

    protected override void OnChildrenCollectionChanged(object? sender,
        System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        RebuildChildren();
        Invalidate();
    }

    public override void Cleanup()
    {
        foreach (var child in _children)
        {
            child.Invalidated -= OnChildInvalidated;
            (child as IIslandComponentCleanup)?.Cleanup();
        }

        _children.Clear();
        base.Cleanup();
    }

    private void RebuildChildren()
    {
        foreach (var child in _children)
        {
            child.Invalidated -= OnChildInvalidated;
            (child as IIslandComponentCleanup)?.Cleanup();
        }

        _children.Clear();

        if (Component.Children != null)
        {
            foreach (var settings in Component.Children)
            {
                var child = IslandComponentFactory.Create(settings, _creationContext);
                if (child == null)
                    continue;
                child.Invalidated += OnChildInvalidated;
                _children.Add(child);
            }
        }
    }

    private void OnChildInvalidated(object? sender, System.EventArgs e) => Invalidate();
}
