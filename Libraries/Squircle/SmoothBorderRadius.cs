using System;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Squircle
{
    /// <summary>
    ///     An immutable set of four <see cref="SmoothCornerRadius" /> values.
    /// </summary>
    /// <remarks>
    ///     Counterpart of <c>SmoothBorderRadius</c> from the Flutter <c>figma_squircle</c> package.
    /// </remarks>
    public readonly struct SmoothBorderRadius : IEquatable<SmoothBorderRadius>
    {
        public static readonly SmoothBorderRadius Zero = new(
            SmoothCornerRadius.Zero,
            SmoothCornerRadius.Zero,
            SmoothCornerRadius.Zero,
            SmoothCornerRadius.Zero);

        public SmoothBorderRadius(
            SmoothCornerRadius topLeft,
            SmoothCornerRadius topRight,
            SmoothCornerRadius bottomLeft,
            SmoothCornerRadius bottomRight)
        {
            TopLeft = topLeft;
            TopRight = topRight;
            BottomLeft = bottomLeft;
            BottomRight = bottomRight;
        }

        /// <summary>Uniform radius on all four corners.</summary>
        public static SmoothBorderRadius All(double cornerRadius, double cornerSmoothing = 0)
        {
            return new SmoothBorderRadius(
                new SmoothCornerRadius(cornerRadius, cornerSmoothing),
                new SmoothCornerRadius(cornerRadius, cornerSmoothing),
                new SmoothCornerRadius(cornerRadius, cornerSmoothing),
                new SmoothCornerRadius(cornerRadius, cornerSmoothing));
        }

        public SmoothCornerRadius TopLeft { get; }
        public SmoothCornerRadius TopRight { get; }
        public SmoothCornerRadius BottomLeft { get; }
        public SmoothCornerRadius BottomRight { get; }

        /// <summary>
        ///     Returns a copy of this radius with the given corners replaced.
        /// </summary>
        public SmoothBorderRadius CopyWith(
            SmoothCornerRadius? topLeft = null,
            SmoothCornerRadius? topRight = null,
            SmoothCornerRadius? bottomLeft = null,
            SmoothCornerRadius? bottomRight = null)
        {
            return new SmoothBorderRadius(
                topLeft ?? TopLeft,
                topRight ?? TopRight,
                bottomLeft ?? BottomLeft,
                bottomRight ?? BottomRight);
        }

        /// <summary>Whether all four corners use zero smoothing (plain rounded rect).</summary>
        public bool IsPlainRoundedRect =>
            TopLeft.CornerSmoothing == 0 &&
            TopRight.CornerSmoothing == 0 &&
            BottomLeft.CornerSmoothing == 0 &&
            BottomRight.CornerSmoothing == 0;

        /// <summary>
        ///     Creates a <see cref="StreamGeometry" /> shaped by this border radius inside <paramref name="rect" />.
        /// </summary>
        public StreamGeometry ToGeometry(Rect rect)
        {
            return SmoothGeometry.Create(rect, this);
        }

        public static SmoothBorderRadius operator +(SmoothBorderRadius a, SmoothBorderRadius b)
        {
            return new SmoothBorderRadius(a.TopLeft + b.TopLeft, a.TopRight + b.TopRight,
                a.BottomLeft + b.BottomLeft, a.BottomRight + b.BottomRight);
        }

        public static SmoothBorderRadius operator -(SmoothBorderRadius a, SmoothBorderRadius b)
        {
            return new SmoothBorderRadius(a.TopLeft - b.TopLeft, a.TopRight - b.TopRight,
                a.BottomLeft - b.BottomLeft, a.BottomRight - b.BottomRight);
        }

        public static SmoothBorderRadius operator *(SmoothBorderRadius a, double scalar)
        {
            return new SmoothBorderRadius(a.TopLeft * scalar, a.TopRight * scalar,
                a.BottomLeft * scalar, a.BottomRight * scalar);
        }

        public static SmoothBorderRadius operator /(SmoothBorderRadius a, double divisor)
        {
            return new SmoothBorderRadius(a.TopLeft / divisor, a.TopRight / divisor,
                a.BottomLeft / divisor, a.BottomRight / divisor);
        }

        /// <summary>Linearly interpolate between two border radii.</summary>
        public static SmoothBorderRadius Lerp(SmoothBorderRadius a, SmoothBorderRadius b, double t)
        {
            return new SmoothBorderRadius(
                SmoothCornerRadius.Lerp(a.TopLeft, b.TopLeft, t),
                SmoothCornerRadius.Lerp(a.TopRight, b.TopRight, t),
                SmoothCornerRadius.Lerp(a.BottomLeft, b.BottomLeft, t),
                SmoothCornerRadius.Lerp(a.BottomRight, b.BottomRight, t));
        }

        public bool Equals(SmoothBorderRadius other)
        {
            return TopLeft.Equals(other.TopLeft) && TopRight.Equals(other.TopRight) &&
                   BottomLeft.Equals(other.BottomLeft) && BottomRight.Equals(other.BottomRight);
        }

        public override bool Equals(object? obj)
        {
            return obj is SmoothBorderRadius other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = TopLeft.GetHashCode();
                hash = (hash * 397) ^ TopRight.GetHashCode();
                hash = (hash * 397) ^ BottomLeft.GetHashCode();
                hash = (hash * 397) ^ BottomRight.GetHashCode();
                return hash;
            }
        }

        public static bool operator ==(SmoothBorderRadius a, SmoothBorderRadius b)
        {
            return a.Equals(b);
        }

        public static bool operator !=(SmoothBorderRadius a, SmoothBorderRadius b)
        {
            return !a.Equals(b);
        }

        public override string ToString()
        {
            if (TopLeft == TopRight && TopLeft == BottomLeft && TopLeft == BottomRight)
                return $"SmoothBorderRadius({TopLeft})";
            return $"SmoothBorderRadius(TopLeft: {TopLeft}, TopRight: {TopRight}, " +
                   $"BottomLeft: {BottomLeft}, BottomRight: {BottomRight})";
        }
    }

    /// <summary>
    ///     Type converter allowing <see cref="SmoothBorderRadius" /> to be set in XAML, e.g.
    ///     <c>&lt;BorderRadius="24,1.0" /&gt;</c> or <c>&lt;BorderRadius="16,16,24,24,0.5" /&gt;</c>.
    /// </summary>
    /// <remarks>
    ///     Accepted formats (comma separated, invariant culture):
    ///     <list type="bullet">
    ///         <item>1 value:  uniform cornerRadius</item>
    ///         <item>2 values: cornerRadius, cornerSmoothing (uniform)</item>
    ///         <item>4 values: tl,tr,bl,br corner radii (uniform smoothing)</item>
    ///         <item>5 values: tl,tr,bl,br radii, uniform smoothing</item>
    ///         <item>8 values: tlR,tlS,trR,trS,blR,blS,brR,brS</item>
    ///     </list>
    /// </remarks>
    public sealed class SmoothBorderRadiusConverter : TypeConverter
    {
        public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
        {
            return sourceType == typeof(string);
        }

        public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
        {
            if (value is string text) return Parse(text);
            return base.ConvertFrom(context, culture, value);
        }

        public override object? ConvertTo(
            ITypeDescriptorContext? context,
            CultureInfo? culture,
            object? value,
            Type destinationType)
        {
            if (destinationType == typeof(string) && value is SmoothBorderRadius r) return r.ToString();
            return base.ConvertTo(context, culture, value, destinationType);
        }

        public static SmoothBorderRadius Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new FormatException("SmoothBorderRadius cannot be parsed from an empty string.");

            var parts = text.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 1) return SmoothBorderRadius.All(ParseDouble(parts[0]));
            if (parts.Length == 2) return SmoothBorderRadius.All(ParseDouble(parts[0]), ParseDouble(parts[1]));
            if (parts.Length == 4)
                return new SmoothBorderRadius(
                    new SmoothCornerRadius(ParseDouble(parts[0]), 0),
                    new SmoothCornerRadius(ParseDouble(parts[1]), 0),
                    new SmoothCornerRadius(ParseDouble(parts[2]), 0),
                    new SmoothCornerRadius(ParseDouble(parts[3]), 0));
            if (parts.Length == 5)
            {
                var smoothing = ParseDouble(parts[4]);
                return new SmoothBorderRadius(
                    new SmoothCornerRadius(ParseDouble(parts[0]), smoothing),
                    new SmoothCornerRadius(ParseDouble(parts[1]), smoothing),
                    new SmoothCornerRadius(ParseDouble(parts[2]), smoothing),
                    new SmoothCornerRadius(ParseDouble(parts[3]), smoothing));
            }

            if (parts.Length == 8)
                return new SmoothBorderRadius(
                    new SmoothCornerRadius(ParseDouble(parts[0]), ParseDouble(parts[1])),
                    new SmoothCornerRadius(ParseDouble(parts[2]), ParseDouble(parts[3])),
                    new SmoothCornerRadius(ParseDouble(parts[4]), ParseDouble(parts[5])),
                    new SmoothCornerRadius(ParseDouble(parts[6]), ParseDouble(parts[7])));

            throw new FormatException(
                $"'{text}' is not a valid SmoothBorderRadius. Expected 1, 2, 4, 5 or 8 comma-separated numbers.");
        }

        private static double ParseDouble(string token)
        {
            if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
                throw new FormatException($"'{token}' is not a valid number.");
            return result;
        }
    }
}