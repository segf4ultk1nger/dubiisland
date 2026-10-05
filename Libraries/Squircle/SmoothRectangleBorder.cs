using System;
using System.Windows;
using System.Windows.Media;

namespace Squircle
{
    /// <summary>Determines where the stroke is painted relative to the shape's boundary.</summary>
    public enum BorderAlign
    {
        Inside,
        Center,
        Outside
    }

    /// <summary>An immutable description of a border side (width + color + style).</summary>
    /// <remarks>Counterpart of Flutter's <c>BorderSide</c>.</remarks>
    public readonly struct SmoothBorderSide : IEquatable<SmoothBorderSide>
    {
        public static readonly SmoothBorderSide None = new(0, Brushes.Transparent, BorderSideStyle.None);

        public SmoothBorderSide(double width, Brush? color = null, BorderSideStyle style = BorderSideStyle.Solid)
        {
            Width = Math.Max(0, width);
            Color = color ?? Brushes.Black;
            Style = style;
        }

        public double Width { get; }
        public Brush Color { get; }
        public BorderSideStyle Style { get; }

        /// <summary>Builds a frozen <see cref="Pen" /> for this side.</summary>
        public Pen ToPen()
        {
            var pen = new Pen(Color, Width)
            {
                StartLineCap = PenLineCap.Square,
                EndLineCap = PenLineCap.Square,
                LineJoin = PenLineJoin.Round
            };
            pen.Freeze();
            return pen;
        }

        public bool Equals(SmoothBorderSide other)
        {
            return Width.Equals(other.Width) && Style == other.Style &&
                   Equals(Color, other.Color);
        }

        public override bool Equals(object? obj)
        {
            return obj is SmoothBorderSide other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = Width.GetHashCode();
                hash = (hash * 397) ^ Style.GetHashCode();
                hash = (hash * 397) ^ (Color?.GetHashCode() ?? 0);
                return hash;
            }
        }

        public static bool operator ==(SmoothBorderSide a, SmoothBorderSide b)
        {
            return a.Equals(b);
        }

        public static bool operator !=(SmoothBorderSide a, SmoothBorderSide b)
        {
            return !a.Equals(b);
        }
    }

    public enum BorderSideStyle
    {
        None,
        Solid
    }

    /// <summary>
    ///     A rectangle border whose corners are smooth squircles.
    /// </summary>
    /// <remarks>
    ///     Counterpart of <c>SmoothRectangleBorder</c> from the Flutter <c>figma_squircle</c> package.
    /// </remarks>
    public sealed class SmoothRectangleBorder
    {
        public SmoothRectangleBorder(
            SmoothBorderSide side = default,
            SmoothBorderRadius borderRadius = default,
            BorderAlign borderAlign = BorderAlign.Center)
        {
            Side = side;
            BorderRadius = borderRadius;
            BorderAlign = borderAlign;
        }

        public SmoothBorderSide Side { get; }
        public SmoothBorderRadius BorderRadius { get; }
        public BorderAlign BorderAlign { get; }

        /// <summary>
        ///     The amount of space the border occupies around the shape, expressed as a <see cref="Thickness" />.
        /// </summary>
        public Thickness Dimensions => BorderAlign switch
        {
            BorderAlign.Inside => new Thickness(Side.Width),
            BorderAlign.Center => new Thickness(Side.Width / 2),
            _ => new Thickness(0)
        };

        /// <summary>Creates a scaled copy of this border.</summary>
        public SmoothRectangleBorder Scale(double t)
        {
            return new SmoothRectangleBorder(
                new SmoothBorderSide(Side.Width * t, Side.Color, Side.Style),
                BorderRadius * t,
                BorderAlign);
        }

        public SmoothRectangleBorder CopyWith(
            SmoothBorderSide? side = null,
            SmoothBorderRadius? borderRadius = null,
            BorderAlign? borderAlign = null)
        {
            return new SmoothRectangleBorder(
                side ?? Side,
                borderRadius ?? BorderRadius,
                borderAlign ?? BorderAlign);
        }

        /// <summary>Creates the geometry of the outer boundary.</summary>
        public StreamGeometry GetOuterGeometry(Rect rect)
        {
            // Always go through the smooth squircle geometry. It handles smoothing == 0
            // (plain rounded rect) too, and applies the same per-corner radius clamping
            // used everywhere else, so oversized radii never self-intersect.
            return BorderRadius.ToGeometry(rect);
        }

        /// <summary>Creates the geometry of the inner boundary (the content area).</summary>
        public StreamGeometry GetInnerGeometry(Rect rect)
        {
            var (innerRect, radius) = GetInnerRectAndRadius(rect);
            return radius.ToGeometry(innerRect);
        }

        /// <summary>Paints the border (and optionally the fill) inside <paramref name="rect" />.</summary>
        public void Paint(DrawingContext dc, Rect rect, Brush? fill = null)
        {
            if (rect.IsEmpty) return;

            // Align-adjust the rect + radius exactly like Flutter's OutlinedBorder.paint.
            var (adjustedRect, adjustedRadius) = GetPaintRectAndRadius(rect);
            var geometry = GetOuterGeometry(adjustedRect, adjustedRadius);

            if (fill != null) dc.DrawGeometry(fill, null, geometry);

            switch (Side.Style)
            {
                case BorderSideStyle.None:
                    break;
                case BorderSideStyle.Solid:
                    dc.DrawGeometry(null, Side.ToPen(), geometry);
                    break;
            }
        }

        private static StreamGeometry GetOuterGeometry(Rect rect, SmoothBorderRadius radius)
        {
            return radius.ToGeometry(rect);
        }

        private (Rect, SmoothBorderRadius) GetInnerRectAndRadius(Rect rect)
        {
            var innerRect = BorderAlign switch
            {
                BorderAlign.Inside => Inflate(rect, -Side.Width),
                BorderAlign.Center => Inflate(rect, -Side.Width / 2),
                _ => rect
            };

            var radius = BorderAlign switch
            {
                BorderAlign.Inside => BorderRadius - SmoothBorderRadius.All(Side.Width, 1),
                BorderAlign.Center => BorderRadius - SmoothBorderRadius.All(Side.Width / 2, 1),
                _ => BorderRadius
            };

            return (innerRect, radius);
        }

        private (Rect, SmoothBorderRadius) GetPaintRectAndRadius(Rect rect)
        {
            var adjustedRect = BorderAlign switch
            {
                BorderAlign.Inside => Inflate(rect, -Side.Width / 2),
                BorderAlign.Center => rect,
                _ => Inflate(rect, Side.Width / 2)
            };

            var adjustedRadius = BorderAlign switch
            {
                BorderAlign.Inside => BorderRadius - SmoothBorderRadius.All(Side.Width / 2, 1),
                BorderAlign.Center => BorderRadius,
                _ => BorderRadius + SmoothBorderRadius.All(Side.Width / 2, 1)
            };

            return (adjustedRect, adjustedRadius);
        }

        private static Rect Inflate(Rect rect, double amount)
        {
            return new Rect(rect.X + amount, rect.Y + amount, Math.Max(0, rect.Width - 2 * amount),
                Math.Max(0, rect.Height - 2 * amount));
        }
    }
}