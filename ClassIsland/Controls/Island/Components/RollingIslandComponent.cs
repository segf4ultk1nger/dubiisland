using System;
using System.Windows;
using System.Windows.Media;
using ClassIsland.Core.Models.Components;
using ClassIsland.Models.ComponentSettings;

namespace ClassIsland.Controls.Island.Components;

/// <summary>滚动组件（等价 RollingComponent.xaml）：内容超宽时横向循环滚动。</summary>
public sealed class RollingIslandComponent : ContainerIslandComponentBase, IIslandComponentCleanup
{
    private const double Gap = 16;

    private double _contentWidth;
    private bool _hooked;
    private bool _started;
    private double _nowSeconds;
    private double _startSeconds;

    public RollingIslandComponent(ComponentSettings component, IslandContext context) : base(component, context)
    {
    }

    private RollingComponentSettings Settings => IslandComponentFactory.GetSettings<RollingComponentSettings>(Component);

    protected override Size MeasureContent(Size availableSize, IslandContext context)
    {
        var size = MeasureChildren(availableSize, context);
        _contentWidth = size.Width;
        return size;
    }

    protected override void RenderContent(DrawingContext drawingContext, Rect slot, IslandContext context)
    {
        if (_contentWidth <= slot.Width + 0.5)
        {
            if (_hooked)
                Unhook();
            RenderChildren(drawingContext, slot, context);
            return;
        }

        if (!_hooked)
        {
            _started = false;
            Hook();
        }

        if (!_started)
        {
            _startSeconds = _nowSeconds;
            _started = true;
        }

        var settings = Settings;
        var speed = Math.Max(1, settings.SpeedPixelPerSecond);
        var pause = settings.IsPauseEnabled ? settings.PauseSeconds : 0;
        var pausePos = settings.IsPauseEnabled ? settings.PauseOffsetX : 0;
        var duration = _contentWidth / speed;
        var total = pause + duration;
        var elapsed = _nowSeconds - _startSeconds;
        var phase = total <= 0 ? 0 : elapsed % total;

        // ponytail: 忽略 PauseOnRule/StopOnRule 规则驱动，需要时接 IConditionPulseService.StatusUpdated 再补。
        var offset = phase < pause ? -pausePos : -pausePos - (phase - pause) * speed;

        drawingContext.PushClip(new RectangleGeometry(slot));
        drawingContext.PushTransform(new TranslateTransform(offset, 0));
        RenderChildren(drawingContext, new Rect(slot.X, slot.Y, _contentWidth, slot.Height), context);
        RenderChildren(drawingContext,
            new Rect(slot.X + _contentWidth + Gap, slot.Y, _contentWidth, slot.Height), context);
        drawingContext.Pop();
        drawingContext.Pop();
    }

    private void Hook()
    {
        if (_hooked)
            return;
        _hooked = true;
        CompositionTarget.Rendering += OnRendering;
    }

    private void Unhook()
    {
        if (!_hooked)
            return;
        _hooked = false;
        CompositionTarget.Rendering -= OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (e is RenderingEventArgs args)
            _nowSeconds = args.RenderingTime.TotalSeconds;
        Invalidate();
    }

    public override void Cleanup()
    {
        Unhook();
        base.Cleanup();
    }
}
