using System;
using System.Windows;
using System.Windows.Media;

namespace ClassIsland.Controls.Island;

/// <summary>
/// 纯自绘表面：拥有单个 <see cref="DrawingVisual"/>，布局与绘制全部交给 <see cref="IslandRenderer"/>。
/// 窗口相对内容固定预留 <see cref="WindowOvershootScale"/> 倍（出现动画弹性过冲峰值），内容在窗口内居中绘制，
/// 避免被窗口边缘裁切。预留恒定，窗口尺寸不随动画变化。
/// </summary>
public sealed class IslandSurface : FrameworkElement
{
    private readonly DrawingVisual _visual = new();
    private readonly IslandRenderer _renderer;

    private double _contentAvailableWidth = double.PositiveInfinity;

    public IslandSurface(IslandRenderer renderer)
    {
        _renderer = renderer;
        AddVisualChild(_visual);
        AddLogicalChild(_visual);
    }

    public IslandRenderer Renderer => _renderer;

    /// <summary>窗口相对内容的固定预留倍数（出现动画弹性过冲峰值），默认 1。窗口尺寸恒定，不随动画变化。</summary>
    public double WindowOvershootScale { get; set; } = 1.0;

    /// <summary>
    ///     内容在预留窗口内的水平对齐（0=左 0.5=中 1=右），同时作为出现动画的缩放锚点。
    ///     与停靠位置一致 → 贴边时岛从贴边一侧向屏幕内生长，过冲不越出屏幕。
    /// </summary>
    public double HorizontalAlign { get; set; } = 0.5;

    /// <summary>内容在预留窗口内的垂直对齐（0=上 1=下），同时作为出现动画的缩放锚点。</summary>
    public double VerticalAlign { get; set; } = 0.5;

    protected override int VisualChildrenCount => 1;

    protected override Visual GetVisualChild(int index) => _visual;

    /// <summary>测量内容所需尺寸（已含窗口缩放与过冲预留），供 HwndSource 决定窗口大小。</summary>
    public Size MeasureContent(Size availableSize)
    {
        var scale = _renderer.Scale;
        _contentAvailableWidth = availableSize.Width / scale;
        var natural = _renderer.Measure(new Size(_contentAvailableWidth, double.PositiveInfinity));
        var overshoot = WindowOvershootScale <= 0 ? 1.0 : WindowOvershootScale;
        return new Size(natural.Width * scale * overshoot, natural.Height * scale * overshoot);
    }

    protected override Size MeasureOverride(Size availableSize) => MeasureContent(availableSize);

    protected override Size ArrangeOverride(Size finalSize)
    {
        Redraw();
        return finalSize;
    }

    /// <summary>按当前尺寸重绘（内容按对齐锚点贴向停靠边，预留余量落在朝向屏幕内的一侧）。</summary>
    public void Redraw()
    {
        var size = RenderSize;
        if (size.Width <= 0 || size.Height <= 0)
            return;

        var scale = _renderer.Scale;
        var natural = _renderer.Measure(new Size(_contentAvailableWidth, double.PositiveInfinity));

        var contentWidth = natural.Width * scale;
        var contentHeight = natural.Height * scale;
        var x = (size.Width - contentWidth) * HorizontalAlign;
        var y = (size.Height - contentHeight) * VerticalAlign;

        var transform = new TransformGroup();
        transform.Children.Add(new ScaleTransform(scale, scale));
        transform.Children.Add(new TranslateTransform(x, y));
        _visual.Transform = transform;

        using var drawingContext = _visual.RenderOpen();
        _renderer.Render(drawingContext, new Rect(natural));
    }
}
