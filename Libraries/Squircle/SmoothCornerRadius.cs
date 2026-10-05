using System;

namespace Squircle
{
    /// <summary>
    ///     A corner radius that additionally carries a <see cref="CornerSmoothing" /> factor,
    ///     mirroring Figma's "Desperately Seeking Squircles" algorithm.
    /// </summary>
    /// <remarks>
    ///     This is the C# counterpart of <c>SmoothRadius</c> from the Flutter
    ///     <c>figma_squircle</c> package.
    /// </remarks>
    public readonly struct SmoothCornerRadius : IEquatable<SmoothCornerRadius>
    {
        public static readonly SmoothCornerRadius Zero = new(0, 0);

        public SmoothCornerRadius(double cornerRadius, double cornerSmoothing)
        {
            CornerRadius = cornerRadius;
            CornerSmoothing = cornerSmoothing;
        }

        public double CornerRadius { get; }

        public double CornerSmoothing { get; }

        public static SmoothCornerRadius operator -(SmoothCornerRadius value)
        {
            return new SmoothCornerRadius(-value.CornerRadius, value.CornerSmoothing);
        }

        public static SmoothCornerRadius operator +(SmoothCornerRadius a, SmoothCornerRadius b)
        {
            return new SmoothCornerRadius(
                a.CornerRadius + b.CornerRadius,
                (a.CornerSmoothing + b.CornerSmoothing) / 2.0);
        }

        public static SmoothCornerRadius operator -(SmoothCornerRadius a, SmoothCornerRadius b)
        {
            return new SmoothCornerRadius(
                a.CornerRadius - b.CornerRadius,
                (a.CornerSmoothing + b.CornerSmoothing) / 2.0);
        }

        public static SmoothCornerRadius operator *(SmoothCornerRadius value, double scalar)
        {
            return new SmoothCornerRadius(value.CornerRadius * scalar, value.CornerSmoothing * scalar);
        }

        public static SmoothCornerRadius operator /(SmoothCornerRadius value, double divisor)
        {
            return new SmoothCornerRadius(value.CornerRadius / divisor, value.CornerSmoothing / divisor);
        }

        public static SmoothCornerRadius operator %(SmoothCornerRadius value, double divisor)
        {
            return new SmoothCornerRadius(value.CornerRadius % divisor, value.CornerSmoothing % divisor);
        }

        /// <summary>
        ///     Linearly interpolate between two smooth radii.
        /// </summary>
        public static SmoothCornerRadius Lerp(SmoothCornerRadius a, SmoothCornerRadius b, double t)
        {
            return new SmoothCornerRadius(
                a.CornerRadius + (b.CornerRadius - a.CornerRadius) * t,
                a.CornerSmoothing + (b.CornerSmoothing - a.CornerSmoothing) * t);
        }

        public bool Equals(SmoothCornerRadius other)
        {
            return Equals(CornerRadius, other.CornerRadius) &&
                   Equals(CornerSmoothing, other.CornerSmoothing);
        }

        public override bool Equals(object? obj)
        {
            return obj is SmoothCornerRadius other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (CornerRadius.GetHashCode() * 397) ^ CornerSmoothing.GetHashCode();
            }
        }

        public static bool operator ==(SmoothCornerRadius a, SmoothCornerRadius b)
        {
            return a.Equals(b);
        }

        public static bool operator !=(SmoothCornerRadius a, SmoothCornerRadius b)
        {
            return !a.Equals(b);
        }

        public override string ToString()
        {
            return $"SmoothCornerRadius(cornerRadius: {CornerRadius.ToString("0.##")}, " +
                   $"cornerSmoothing: {CornerSmoothing.ToString("0.##")})";
        }
    }
}