using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using ClassIsland.Core.Models.Components;
using ClassIsland.Models.ComponentSettings;

namespace ClassIsland.Controls.Island.Components;

/// <summary>轮播组件（等价 SlideComponent.xaml）：在多个子组件间定时切换。</summary>
public sealed class SlideIslandComponent : ContainerIslandComponentBase, IIslandComponentCleanup
{
    private readonly DispatcherTimer _timer = new();
    private int _index;

    public SlideIslandComponent(ComponentSettings component, IslandContext context) : base(component, context)
    {
        _timer.Tick += (_, _) => Advance();
        UpdateTimer();
    }

    private SlideComponentSettings Settings => IslandComponentFactory.GetSettings<SlideComponentSettings>(Component);

    private List<IIslandComponent> VisibleChildren => Children.Where(c => c.IsVisible).ToList();

    protected override Size MeasureContent(Size availableSize, IslandContext context)
    {
        var width = 0.0;
        var height = 40.0;
        foreach (var child in VisibleChildren)
        {
            var size = child.Measure(availableSize, context);
            width = Math.Max(width, size.Width);
            height = Math.Max(height, size.Height);
        }

        UpdateTimer();
        return new Size(width, height);
    }

    protected override void RenderContent(DrawingContext drawingContext, Rect slot, IslandContext context)
    {
        var visible = VisibleChildren;
        if (visible.Count == 0)
            return;
        if (_index >= visible.Count)
            _index = 0;
        visible[_index].Render(drawingContext, slot, context);
    }

    private void UpdateTimer()
    {
        if (VisibleChildren.Count <= 1)
        {
            _timer.Stop();
            return;
        }

        _timer.Interval = TimeSpan.FromSeconds(Math.Max(1, Settings.SlideSeconds));
        if (!_timer.IsEnabled)
            _timer.Start();
    }

    private void Advance()
    {
        // ponytail: 只做循环轮播（SlideMode 0），随机/往复模式需要时再补。
        var count = VisibleChildren.Count;
        if (count > 1)
            _index = (_index + 1) % count;
        Invalidate();
    }

    public override void Cleanup()
    {
        _timer.Stop();
        base.Cleanup();
    }
}
