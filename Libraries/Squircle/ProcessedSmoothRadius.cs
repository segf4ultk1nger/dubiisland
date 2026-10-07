using System;
using System.Collections.Concurrent;

namespace Squircle
{
    /// <summary>
    ///     Pre-computed Bézier + arc parameters for a single smooth corner.
    /// </summary>
    /// <remarks>
    ///     Ported from the Flutter <c>figma_squircle</c> package
    ///     (<c>ProcessedSmoothRadius</c>), which is based on the Figma blog post
    ///     "Desperately Seeking Squircles".
    /// </remarks>
    public sealed class ProcessedSmoothRadius
    {
        private static readonly ConcurrentDictionary<SmoothCoreKey, ProcessedSmoothRadius> Cache = new();
        private const int MaxCacheSize = 1024;

        private ProcessedSmoothRadius(
            double a,
            double b,
            double c,
            double d,
            double p,
            double width,
            double height,
            double circularSectionLength,
            SmoothCornerRadius radius)
        {
            this.a = a;
            this.b = b;
            this.c = c;
            this.d = d;
            this.p = p;
            this.width = width;
            this.height = height;
            CircularSectionLength = circularSectionLength;
            Radius = radius;
        }

        public SmoothCornerRadius Radius { get; }

        public double a { get; }
        public double b { get; }
        public double c { get; }
        public double d { get; }
        public double p { get; }
        public double width { get; }
        public double height { get; }

        /// <summary>
        ///     The chord length of the circular section (arc) of the corner.
        /// </summary>
        public double CircularSectionLength { get; }

        /// <summary>
        ///     Creates a <see cref="ProcessedSmoothRadius" /> for the given corner radius and size.
        ///     Results are cached keyed by (radius, width, height).
        /// </summary>
        public static ProcessedSmoothRadius Create(
            SmoothCornerRadius radius,
            double width,
            double height,
            bool useCache = true)
        {
            var key = new SmoothCoreKey(radius.CornerRadius, radius.CornerSmoothing, width, height);
            if (useCache && Cache.TryGetValue(key, out var cached)) return cached;

            // Guard against invalid dimensions / out-of-range inputs so the math below
            // never produces NaN or self-intersecting control points.
            if (width <= 0 || height <= 0) radius = SmoothCornerRadius.Zero;

            var cornerSmoothing = Math.Max(0, Math.Min(1, radius.CornerSmoothing));
            var maxRadius = Math.Min(width, height) / 2.0;
            var cornerRadius = Math.Max(0, Math.Min(radius.CornerRadius, maxRadius));

            // 12.2 from the article.
            var p = Math.Min((1 + cornerSmoothing) * cornerRadius, maxRadius);

            double angleAlpha, angleBeta;

            if (cornerRadius <= maxRadius / 2.0)
            {
                angleBeta = 90 * (1 - cornerSmoothing);
                angleAlpha = 45 * cornerSmoothing;
            }
            else
            {
                var diffRatio = maxRadius > 0 ? (cornerRadius - maxRadius / 2.0) / (maxRadius / 2.0) : 0;
                angleBeta = 90 * (1 - cornerSmoothing * (1 - diffRatio));
                angleAlpha = 45 * cornerSmoothing * (1 - diffRatio);
            }

            var angleTheta = (90 - angleBeta) / 2.0;

            // "p3ToP4Distance": distance between the two control points P3 and P4.
            var p3ToP4Distance = cornerRadius * Math.Tan(MathExtra.DegToRad(angleTheta / 2.0));

            var circularSectionLength =
                Math.Sin(MathExtra.DegToRad(angleBeta / 2.0)) * cornerRadius * Math.Sqrt(2.0);

            // a, b, c and d are from 11.1 in the article. Clamp b to non-negative so the
            // Bezier control points never reverse past the arc and create a knotted curve.
            var c = p3ToP4Distance * Math.Cos(MathExtra.DegToRad(angleAlpha));
            var d = c * Math.Tan(MathExtra.DegToRad(angleAlpha));
            var b = Math.Max(0, (p - circularSectionLength - c - d) / 3.0);
            var a = 2 * b;

            var result = new ProcessedSmoothRadius(
                a,
                b,
                c,
                d,
                p,
                width,
                height,
                circularSectionLength,
                new SmoothCornerRadius(cornerRadius, cornerSmoothing));

            if (useCache)
            {
                if (Cache.Count >= MaxCacheSize)
                    Cache.Clear(); // 容量上限，避免不同尺寸长期累积
                Cache[key] = result;
            }

            return result;
        }

        private readonly struct SmoothCoreKey : IEquatable<SmoothCoreKey>
        {
            public SmoothCoreKey(double cornerRadius, double smoothing, double width, double height)
            {
                CornerRadius = cornerRadius;
                Smoothing = smoothing;
                Width = width;
                Height = height;
            }

            public double CornerRadius { get; }
            public double Smoothing { get; }
            public double Width { get; }
            public double Height { get; }

            public bool Equals(SmoothCoreKey other)
            {
                return Equals(CornerRadius, other.CornerRadius) &&
                       Equals(Smoothing, other.Smoothing) &&
                       Equals(Width, other.Width) &&
                       Equals(Height, other.Height);
            }

            public override bool Equals(object? obj)
            {
                return obj is SmoothCoreKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = CornerRadius.GetHashCode();
                    hash = (hash * 397) ^ Smoothing.GetHashCode();
                    hash = (hash * 397) ^ Width.GetHashCode();
                    hash = (hash * 397) ^ Height.GetHashCode();
                    return hash;
                }
            }
        }
    }

    internal static class MathExtra
    {
        public static double DegToRad(double degrees)
        {
            return degrees * Math.PI / 180.0;
        }
    }
}