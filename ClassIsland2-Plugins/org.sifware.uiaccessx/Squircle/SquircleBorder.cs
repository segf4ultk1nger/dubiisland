using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Org.Sifware.UiAccessX.Squircle;

/// <summary>
/// 自绘超椭圆背景：阴影（圆角矩形）+ 超椭圆填充 + 超椭圆描边。
/// 作为宿主 <c>Border.line-background</c> 的子元素铺满其内容区，接管其全部绘制。
/// </summary>
internal sealed class SquircleBorder : Control
{
    public static readonly StyledProperty<double> CornerRadiusProperty =
        AvaloniaProperty.Register<SquircleBorder, double>(nameof(CornerRadius));

    public static readonly StyledProperty<double> SmoothingProperty =
        AvaloniaProperty.Register<SquircleBorder, double>(nameof(Smoothing), 0.6);

    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<SquircleBorder, IBrush?>(nameof(Fill));

    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<SquircleBorder, IBrush?>(nameof(Stroke));

    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<SquircleBorder, double>(nameof(StrokeThickness), 1.0);

    public static readonly StyledProperty<BoxShadows> BoxShadowProperty =
        AvaloniaProperty.Register<SquircleBorder, BoxShadows>(nameof(BoxShadow));

    static SquircleBorder()
    {
        AffectsRender<SquircleBorder>(
            CornerRadiusProperty, SmoothingProperty, FillProperty,
            StrokeProperty, StrokeThicknessProperty, BoxShadowProperty);
    }

    public double CornerRadius
    {
        get => GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public double Smoothing
    {
        get => GetValue(SmoothingProperty);
        set => SetValue(SmoothingProperty, value);
    }

    public IBrush? Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public double StrokeThickness
    {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public BoxShadows BoxShadow
    {
        get => GetValue(BoxShadowProperty);
        set => SetValue(BoxShadowProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var size = Bounds.Size;
        if (size.Width <= 0 || size.Height <= 0)
        {
            return;
        }

        var rect = new Rect(size);

        // 阴影用圆角矩形绘制（模糊后与超椭圆的差异不可见）。
        context.DrawRectangle(null, null, rect, CornerRadius, CornerRadius, BoxShadow);

        var geometry = SquircleGeometry.Create(size, CornerRadius, Smoothing);
        IPen? pen = Stroke is { } stroke && StrokeThickness > 0
            ? new Pen(stroke, StrokeThickness)
            : null;
        context.DrawGeometry(Fill, pen, geometry);
    }
}
