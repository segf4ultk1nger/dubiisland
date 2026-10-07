using System;
using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Media;

namespace Squircle
{
    /// <summary>
    ///     Builds a <see cref="StreamGeometry" /> shaped as a rectangle with smooth corners,
    ///     using the Figma "squircle" approximation (two cubic Béziers + one arc per corner).
    /// </summary>
    /// <remarks>Ported from <c>path_smooth_corners.dart</c>.</remarks>
    public static class SmoothGeometry
    {
        private static readonly ConcurrentDictionary<GeometryKey, StreamGeometry> Cache = new();
        private const int MaxCacheSize = 1024;

        /// <summary>
        ///     Creates a smooth-cornered <see cref="StreamGeometry" /> inside <paramref name="rect" />.
        ///     Uniform corner radius on all four corners.
        /// </summary>
        public static StreamGeometry Create(Rect rect, SmoothCornerRadius cornerRadius, bool useCache = true)
        {
            var radius = new SmoothBorderRadius(
                cornerRadius,
                cornerRadius,
                cornerRadius,
                cornerRadius);
            return Create(rect, radius, useCache);
        }

        /// <summary>
        ///     Creates a smooth-cornered <see cref="StreamGeometry" /> inside <paramref name="rect" />.
        /// </summary>
        public static StreamGeometry Create(Rect rect, SmoothBorderRadius radius, bool useCache = true)
        {
            var width = rect.Width;
            var height = rect.Height;

            var key = new GeometryKey(
                radius.TopLeft, radius.TopRight,
                radius.BottomLeft, radius.BottomRight,
                width, height);

            StreamGeometry geometry;
            if (useCache && Cache.TryGetValue(key, out var cached))
            {
                geometry = cached;
            }
            else
            {
                geometry = BuildLocal(radius, width, height);
                if (useCache)
                {
                    if (Cache.Count >= MaxCacheSize)
                        Cache.Clear(); // 容量上限，避免不同尺寸长期累积
                    Cache[key] = geometry;
                }
            }

            if (rect.X == 0 && rect.Y == 0) return geometry;

            // Geometry is cached in local coords (0,0)-(width,height); shift to the requested position.
            var positioned = geometry.Clone();
            positioned.Transform = new TranslateTransform(rect.X, rect.Y);
            positioned.Freeze();
            return positioned;
        }

        private static StreamGeometry BuildLocal(SmoothBorderRadius radius, double width, double height)
        {
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                // Share processed radii when adjacent corners are equal to avoid duplicate math.
                var processedTopLeft = ProcessedSmoothRadius.Create(radius.TopLeft, width, height);
                var processedBottomLeft = radius.TopLeft == radius.BottomLeft
                    ? processedTopLeft
                    : ProcessedSmoothRadius.Create(radius.BottomLeft, width, height);
                var processedBottomRight = radius.BottomLeft == radius.BottomRight
                    ? processedBottomLeft
                    : ProcessedSmoothRadius.Create(radius.BottomRight, width, height);
                var processedTopRight = radius.TopRight == radius.BottomRight
                    ? processedBottomRight
                    : ProcessedSmoothRadius.Create(radius.TopRight, width, height);

                CreateGeometry(ctx,
                    processedTopLeft, processedTopRight,
                    processedBottomLeft, processedBottomRight,
                    width, height);
            }

            geometry.Freeze();
            return geometry;
        }

        private static void CreateGeometry(
            StreamGeometryContext ctx,
            ProcessedSmoothRadius processedTopLeft,
            ProcessedSmoothRadius processedTopRight,
            ProcessedSmoothRadius processedBottomLeft,
            ProcessedSmoothRadius processedBottomRight,
            double width,
            double height)
        {
            AddTopRight(ctx, processedTopRight, width, height);
            AddBottomRight(ctx, processedBottomRight, width, height);
            AddBottomLeft(ctx, processedBottomLeft, width, height);
            AddTopLeft(ctx, processedTopLeft, width, height);
            ctx.Close();
        }

        // WPF's ArcTo throws on a degenerate (zero-length) arc, which happens when
        // cornerSmoothing == 1 (angleBeta == 0, so circularSectionLength == 0).
        private static void SmoothArc(StreamGeometryContext ctx, Point end, double cornerRadius, double csl)
        {
            if (csl > 0.0)
                ctx.ArcTo(end, new Size(cornerRadius, cornerRadius), 0, false,
                    SweepDirection.Clockwise, true, false);
        }

        private static void AddTopRight(StreamGeometryContext ctx, ProcessedSmoothRadius r, double w, double h)
        {
            if (r.Radius.CornerRadius > 0)
            {
                var a = r.a;
                var b = r.b;
                var c = r.c;
                var d = r.d;
                var p = r.p;
                var csl = r.CircularSectionLength;
                var cr = r.Radius.CornerRadius;

                ctx.BeginFigure(new Point(Math.Max(w / 2, w - p), 0), true, true);
                ctx.BezierTo(
                    new Point(w - (p - a), 0),
                    new Point(w - (p - a - b), 0),
                    new Point(w - (p - a - b - c), d), true, false);
                SmoothArc(ctx, new Point(w - (p - a - b - c) + csl, d + csl), cr, csl);
                ctx.BezierTo(
                    new Point(w, p - a - b),
                    new Point(w, p - a),
                    new Point(w, Math.Min(h / 2, p)), true, false);
            }
            else
            {
                ctx.BeginFigure(new Point(w / 2, 0), true, true);
                ctx.LineTo(new Point(w, 0), true, false);
                ctx.LineTo(new Point(w, h / 2), true, false);
            }
        }

        private static void AddBottomRight(StreamGeometryContext ctx, ProcessedSmoothRadius r, double w, double h)
        {
            if (r.Radius.CornerRadius > 0)
            {
                var a = r.a;
                var b = r.b;
                var c = r.c;
                var d = r.d;
                var p = r.p;
                var csl = r.CircularSectionLength;
                var cr = r.Radius.CornerRadius;

                ctx.LineTo(new Point(w, Math.Max(h / 2, h - p)), true, false);
                ctx.BezierTo(
                    new Point(w, h - (p - a)),
                    new Point(w, h - (p - a - b)),
                    new Point(w - d, h - (p - a - b - c)), true, false);
                SmoothArc(ctx, new Point(w - d - csl, h - (p - a - b - c) + csl), cr, csl);
                ctx.BezierTo(
                    new Point(w - (p - a - b), h),
                    new Point(w - (p - a), h),
                    new Point(Math.Max(w / 2, w - p), h), true, false);
            }
            else
            {
                ctx.LineTo(new Point(w, h), true, false);
                ctx.LineTo(new Point(w / 2, h), true, false);
            }
        }

        private static void AddBottomLeft(StreamGeometryContext ctx, ProcessedSmoothRadius r, double w, double h)
        {
            if (r.Radius.CornerRadius > 0)
            {
                var a = r.a;
                var b = r.b;
                var c = r.c;
                var d = r.d;
                var p = r.p;
                var csl = r.CircularSectionLength;
                var cr = r.Radius.CornerRadius;

                ctx.LineTo(new Point(Math.Min(w / 2, p), h), true, false);
                ctx.BezierTo(
                    new Point(p - a, h),
                    new Point(p - a - b, h),
                    new Point(p - a - b - c, h - d), true, false);
                SmoothArc(ctx, new Point(p - a - b - c - csl, h - d - csl), cr, csl);
                ctx.BezierTo(
                    new Point(0, h - (p - a - b)),
                    new Point(0, h - (p - a)),
                    new Point(0, Math.Max(h / 2, h - p)), true, false);
            }
            else
            {
                ctx.LineTo(new Point(0, h), true, false);
                ctx.LineTo(new Point(0, h / 2), true, false);
            }
        }

        private static void AddTopLeft(StreamGeometryContext ctx, ProcessedSmoothRadius r, double w, double h)
        {
            if (r.Radius.CornerRadius > 0)
            {
                var a = r.a;
                var b = r.b;
                var c = r.c;
                var d = r.d;
                var p = r.p;
                var csl = r.CircularSectionLength;
                var cr = r.Radius.CornerRadius;

                ctx.LineTo(new Point(0, Math.Min(h / 2, p)), true, false);
                ctx.BezierTo(
                    new Point(0, p - a),
                    new Point(0, p - a - b),
                    new Point(d, p - a - b - c), true, false);
                SmoothArc(ctx, new Point(d + csl, p - a - b - c - csl), cr, csl);
                ctx.BezierTo(
                    new Point(p - a - b, 0),
                    new Point(p - a, 0),
                    new Point(Math.Min(w / 2, p), 0), true, false);
            }
            else
            {
                ctx.LineTo(new Point(0, 0), true, false);
            }
        }

        private readonly struct GeometryKey : IEquatable<GeometryKey>
        {
            private readonly SmoothCornerRadius _tl, _tr, _bl, _br;
            private readonly double _w, _h;

            public GeometryKey(
                SmoothCornerRadius tl, SmoothCornerRadius tr,
                SmoothCornerRadius bl, SmoothCornerRadius br,
                double w, double h)
            {
                _tl = tl;
                _tr = tr;
                _bl = bl;
                _br = br;
                _w = w;
                _h = h;
            }

            public bool Equals(GeometryKey other)
            {
                return _tl.Equals(other._tl) && _tr.Equals(other._tr) &&
                       _bl.Equals(other._bl) && _br.Equals(other._br) &&
                       _w.Equals(other._w) && _h.Equals(other._h);
            }

            public override bool Equals(object? obj)
            {
                return obj is GeometryKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = _tl.GetHashCode();
                    hash = (hash * 397) ^ _tr.GetHashCode();
                    hash = (hash * 397) ^ _bl.GetHashCode();
                    hash = (hash * 397) ^ _br.GetHashCode();
                    hash = (hash * 397) ^ _w.GetHashCode();
                    hash = (hash * 397) ^ _h.GetHashCode();
                    return hash;
                }
            }
        }
    }
}