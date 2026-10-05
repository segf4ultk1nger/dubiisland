using System;
using System.Windows;
using System.Windows.Media;

namespace ClassIsland.Controls.Island;

/// <summary>
/// 纯自绘表面：拥有单个 <see cref="DrawingVisual"/>，布局与绘制全部交给 <see cref="IslandRenderer"/>。
/// </summary>
public sealed class IslandSurface : FrameworkElement
{
    private readonly DrawingVisual _visual = new();
    private readonly IslandRenderer _renderer;

    public IslandSurface(IslandRenderer renderer)
    {
        _renderer = renderer;
        _renderer.Invalidated += OnRendererInvalidated;
        AddVisualChild(_visual);
        AddLogicalChild(_visual);
    }

    public IslandRenderer Renderer => _renderer;

    protected override int VisualChildrenCount => 1;

    protected override Visual GetVisualChild(int index) => _visual;

    /// <summary>测量内容所需尺寸（已含窗口缩放），供 HwndSource 决定窗口大小。</summary>
    public Size MeasureContent(Size availableSize)
    {
        var scale = _renderer.Scale;
        var natural = _renderer.Measure(new Size(availableSize.Width / scale, double.PositiveInfinity));
        return new Size(natural.Width * scale, natural.Height * scale);
    }

    protected override Size MeasureOverride(Size availableSize) => MeasureContent(availableSize);

    protected override Size ArrangeOverride(Size finalSize)
    {
        Redraw(finalSize);
        return finalSize;
    }

    /// <summary>按当前尺寸重绘。</summary>
    public void Redraw() => Redraw(RenderSize);

    private void Redraw(Size size)
    {
        if (size.Width <= 0 || size.Height <= 0)
            return;

        var scale = _renderer.Scale;
        var natural = _renderer.Measure(new Size(size.Width / scale, double.PositiveInfinity));
        _visual.Transform = new ScaleTransform(scale, scale);
        using var drawingContext = _visual.RenderOpen();
        _renderer.Render(drawingContext, new Rect(natural));
    }

    private void OnRendererInvalidated(object? sender, EventArgs e) => Redraw();
}
