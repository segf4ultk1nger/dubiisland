using Avalonia;
using Avalonia.Media;

namespace Org.Sifware.UiAccessX.Squircle;

/// <summary>圆角半径 + 平滑度，对应 Figma「Desperately Seeking Squircles」的平滑角。</summary>
internal readonly struct SmoothCornerRadius
{
    public static readonly SmoothCornerRadius Zero = new(0, 0);

    public SmoothCornerRadius(double cornerRadius, double cornerSmoothing)
    {
        CornerRadius = cornerRadius;
        CornerSmoothing = cornerSmoothing;
    }

    public double CornerRadius { get; }

    public double CornerSmoothing { get; }
}

/// <summary>单个平滑角预计算出的「两段三次贝塞尔 + 一段圆弧」参数。</summary>
internal readonly struct ProcessedSmoothRadius
{
    public double A { get; init; }
    public double B { get; init; }
    public double C { get; init; }
    public double D { get; init; }
    public double P { get; init; }
    public double CircularSectionLength { get; init; }
    public double Radius { get; init; }

    public static ProcessedSmoothRadius Create(double cornerRadius, double smoothing, double width, double height)
    {
        if (width <= 0 || height <= 0)
        {
            cornerRadius = 0;
            smoothing = 0;
        }

        smoothing = Math.Max(0, Math.Min(1, smoothing));
        var maxRadius = Math.Min(width, height) / 2.0;
        cornerRadius = Math.Max(0, Math.Min(cornerRadius, maxRadius));

        var p = Math.Min((1 + smoothing) * cornerRadius, maxRadius);

        double angleAlpha, angleBeta;
        if (cornerRadius <= maxRadius / 2.0)
        {
            angleBeta = 90 * (1 - smoothing);
            angleAlpha = 45 * smoothing;
        }
        else
        {
            var diffRatio = maxRadius > 0 ? (cornerRadius - maxRadius / 2.0) / (maxRadius / 2.0) : 0;
            angleBeta = 90 * (1 - smoothing * (1 - diffRatio));
            angleAlpha = 45 * smoothing * (1 - diffRatio);
        }

        var angleTheta = (90 - angleBeta) / 2.0;
        var p3ToP4Distance = cornerRadius * Math.Tan(DegToRad(angleTheta / 2.0));
        var circularSectionLength = Math.Sin(DegToRad(angleBeta / 2.0)) * cornerRadius * Math.Sqrt(2.0);

        var c = p3ToP4Distance * Math.Cos(DegToRad(angleAlpha));
        var d = c * Math.Tan(DegToRad(angleAlpha));
        var b = Math.Max(0, (p - circularSectionLength - c - d) / 3.0);
        var a = 2 * b;

        return new ProcessedSmoothRadius
        {
            A = a,
            B = b,
            C = c,
            D = d,
            P = p,
            CircularSectionLength = circularSectionLength,
            Radius = cornerRadius
        };
    }

    private static double DegToRad(double degrees) => degrees * Math.PI / 180.0;
}

/// <summary>
/// 生成超椭圆（squircle）矩形几何：每角两段三次贝塞尔 + 一段圆弧。
/// 移植自 LegacyIsland 的 <c>Libraries/Squircle/SmoothGeometry</c>（WPF → Avalonia）。
/// </summary>
internal static class SquircleGeometry
{
    public static StreamGeometry Create(Size size, double cornerRadius, double smoothing)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            var r = ProcessedSmoothRadius.Create(cornerRadius, smoothing, size.Width, size.Height);
            AddTopRight(ctx, r, size.Width, size.Height);
            AddBottomRight(ctx, r, size.Width, size.Height);
            AddBottomLeft(ctx, r, size.Width, size.Height);
            AddTopLeft(ctx, r, size.Width, size.Height);
            ctx.EndFigure(true);
        }

        return geometry;
    }

    private static void SmoothArc(StreamGeometryContext ctx, Point end, double cornerRadius, double csl)
    {
        // 退化（零长）圆弧在 cornerSmoothing == 1 时出现，直接跳过。
        if (csl > 0.0)
        {
            ctx.ArcTo(end, new Size(cornerRadius, cornerRadius), 0, false, SweepDirection.Clockwise);
        }
    }

    private static void AddTopRight(StreamGeometryContext ctx, ProcessedSmoothRadius r, double w, double h)
    {
        if (r.Radius > 0)
        {
            var (a, b, c, d, p, csl) = (r.A, r.B, r.C, r.D, r.P, r.CircularSectionLength);
            ctx.BeginFigure(new Point(Math.Max(w / 2, w - p), 0), true);
            ctx.CubicBezierTo(new Point(w - (p - a), 0), new Point(w - (p - a - b), 0), new Point(w - (p - a - b - c), d));
            SmoothArc(ctx, new Point(w - (p - a - b - c) + csl, d + csl), r.Radius, csl);
            ctx.CubicBezierTo(new Point(w, p - a - b), new Point(w, p - a), new Point(w, Math.Min(h / 2, p)));
        }
        else
        {
            ctx.BeginFigure(new Point(w / 2, 0), true);
            ctx.LineTo(new Point(w, 0));
            ctx.LineTo(new Point(w, h / 2));
        }
    }

    private static void AddBottomRight(StreamGeometryContext ctx, ProcessedSmoothRadius r, double w, double h)
    {
        if (r.Radius > 0)
        {
            var (a, b, c, d, p, csl) = (r.A, r.B, r.C, r.D, r.P, r.CircularSectionLength);
            ctx.LineTo(new Point(w, Math.Max(h / 2, h - p)));
            ctx.CubicBezierTo(new Point(w, h - (p - a)), new Point(w, h - (p - a - b)), new Point(w - d, h - (p - a - b - c)));
            SmoothArc(ctx, new Point(w - d - csl, h - (p - a - b - c) + csl), r.Radius, csl);
            ctx.CubicBezierTo(new Point(w - (p - a - b), h), new Point(w - (p - a), h), new Point(Math.Max(w / 2, w - p), h));
        }
        else
        {
            ctx.LineTo(new Point(w, h));
            ctx.LineTo(new Point(w / 2, h));
        }
    }

    private static void AddBottomLeft(StreamGeometryContext ctx, ProcessedSmoothRadius r, double w, double h)
    {
        if (r.Radius > 0)
        {
            var (a, b, c, d, p, csl) = (r.A, r.B, r.C, r.D, r.P, r.CircularSectionLength);
            ctx.LineTo(new Point(Math.Min(w / 2, p), h));
            ctx.CubicBezierTo(new Point(p - a, h), new Point(p - a - b, h), new Point(p - a - b - c, h - d));
            SmoothArc(ctx, new Point(p - a - b - c - csl, h - d - csl), r.Radius, csl);
            ctx.CubicBezierTo(new Point(0, h - (p - a - b)), new Point(0, h - (p - a)), new Point(0, Math.Max(h / 2, h - p)));
        }
        else
        {
            ctx.LineTo(new Point(0, h));
            ctx.LineTo(new Point(0, h / 2));
        }
    }

    private static void AddTopLeft(StreamGeometryContext ctx, ProcessedSmoothRadius r, double w, double h)
    {
        if (r.Radius > 0)
        {
            var (a, b, c, d, p, csl) = (r.A, r.B, r.C, r.D, r.P, r.CircularSectionLength);
            ctx.LineTo(new Point(0, Math.Min(h / 2, p)));
            ctx.CubicBezierTo(new Point(0, p - a), new Point(0, p - a - b), new Point(d, p - a - b - c));
            SmoothArc(ctx, new Point(d + csl, p - a - b - c - csl), r.Radius, csl);
            ctx.CubicBezierTo(new Point(p - a - b, 0), new Point(p - a, 0), new Point(Math.Min(w / 2, p), 0));
        }
        else
        {
            ctx.LineTo(new Point(0, 0));
        }
    }
}
