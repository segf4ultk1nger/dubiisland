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

    /// <summary>
    ///     一次性测量：返回目标内容尺寸（DIP，供停靠/定位）与窗口尺寸
    ///     （已含缩放、出现动画过冲与几何补间预留，供 HwndSource 决定窗口大小）。
    /// </summary>
    public (Size Target, Size Window) MeasureForWindow(Size availableSize)
    {
        var scale = _renderer.Scale;
        _contentAvailableWidth = availableSize.Width / scale;
        var natural = _renderer.Measure(new Size(_contentAvailableWidth, double.PositiveInfinity));
        var overshoot = WindowOvershootScale <= 0 ? 1.0 : WindowOvershootScale;
        var reservedW = Math.Max(natural.Width, _renderer.Animator?.ReservedWidth ?? 0);
        var reservedH = Math.Max(natural.Height, _renderer.Animator?.ReservedHeight ?? 0);
        return (
            new Size(natural.Width * scale, natural.Height * scale),
            new Size(reservedW * scale * overshoot, reservedH * scale * overshoot));
    }

    /// <summary>测量窗口所需尺寸（已含窗口缩放与过冲/补间预留），供 HwndSource 决定窗口大小。</summary>
    public Size MeasureContent(Size availableSize) => MeasureForWindow(availableSize).Window;

    protected override Size MeasureOverride(Size availableSize) => MeasureContent(availableSize);

    protected override Size ArrangeOverride(Size finalSize)
    {
        Redraw();
        return finalSize;
    }

    /// <summary>按当前尺寸重绘（内容按对齐锚点贴向停靠边，预留余量落在朝向屏幕内的一侧）。</summary>
    public void Redraw() => Redraw(RenderSize);

    /// <summary>
    ///     按指定尺寸重绘。用于窗口尺寸刚被 SetWindowPos 改变、WPF 尚未刷新 <see cref="FrameworkElement.RenderSize"/>
    ///     的瞬间：若此时用过期的 RenderSize 做对齐，居中/贴边内容会先偏移一帧、再被布局层纠正，表现为闪烁。
    /// </summary>
    public void Redraw(Size size)
    {
        if (size.Width <= 0 || size.Height <= 0)
            return;

        var scale = _renderer.Scale;
        var natural = _renderer.Measure(new Size(_contentAvailableWidth, double.PositiveInfinity));

        var contentWidth = natural.Width * scale;
        var contentHeight = natural.Height * scale;
        var x = (size.Width - contentWidth) * HorizontalAlign;
        var y = (size.Height - contentHeight) * VerticalAlign;

        var transform = new TransformGroup();
        var stretch = _renderer.Animator?.GetMotionStretch() ?? 0;
        if (Math.Abs(stretch) > 0.0001)
        {
            // 限制形变幅度不超过窗口已预留的透明余量，避免被窗口边缘裁切。
            var capX = Math.Max(0, size.Width - contentWidth) / contentWidth;
            var capY = Math.Max(0, size.Height - contentHeight) / contentHeight;
            var cap = Math.Min(capX, capY);
            var d = stretch > cap ? cap : stretch < -cap ? -cap : stretch;
            if (Math.Abs(d) > 0.0001)
            {
                // 以停靠对齐点为锚点：贴边时向屏幕内拉伸，不出屏、也不越出窗口预留。
                transform.Children.Add(new ScaleTransform(
                    1 + d, 1 - d,
                    natural.Width * HorizontalAlign, natural.Height * VerticalAlign));
            }
        }

        transform.Children.Add(new ScaleTransform(scale, scale));
        transform.Children.Add(new TranslateTransform(x, y));
        _visual.Transform = transform;

        using var drawingContext = _visual.RenderOpen();
        _renderer.Render(drawingContext, new Rect(natural));
    }
}
