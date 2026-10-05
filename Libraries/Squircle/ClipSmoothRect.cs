using System;
using System.Windows;

namespace Squircle
{
    /// <summary>
    ///     Attached properties that clip any <see cref="FrameworkElement" /> to a smooth
    ///     "squircle" shape. This is the WPF equivalent of Flutter's <c>ClipSmoothRect</c>.
    /// </summary>
    /// <remarks>
    ///     Usage: <c>&lt;Image Squircle.SmoothClip.CornerRadius="16" Squircle.SmoothClip.CornerSmoothing="1" .../&gt;</c>
    /// </remarks>
    public static class SmoothClip
    {
        public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.RegisterAttached(
            "CornerRadius", typeof(double), typeof(SmoothClip),
            new FrameworkPropertyMetadata(0.0, OnPropertyChanged, CoerceCornerRadius));

        public static readonly DependencyProperty CornerSmoothingProperty = DependencyProperty.RegisterAttached(
            "CornerSmoothing", typeof(double), typeof(SmoothClip),
            new FrameworkPropertyMetadata(0.0, OnPropertyChanged));

        public static double GetCornerRadius(DependencyObject target)
        {
            return (double)target.GetValue(CornerRadiusProperty);
        }

        public static void SetCornerRadius(DependencyObject target, double value)
        {
            target.SetValue(CornerRadiusProperty, value);
        }

        public static double GetCornerSmoothing(DependencyObject target)
        {
            return (double)target.GetValue(CornerSmoothingProperty);
        }

        public static void SetCornerSmoothing(DependencyObject target, double value)
        {
            target.SetValue(CornerSmoothingProperty, value);
        }

        private static object CoerceCornerRadius(DependencyObject d, object baseValue)
        {
            return Math.Max(0.0, (double)baseValue);
        }

        private static void OnPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is FrameworkElement element)
            {
                element.SizeChanged += OnSizeChanged;
                if (element.IsLoaded)
                    ApplyClip(element);
                else
                    element.Loaded += OnLoaded;
            }
        }

        private static void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (sender is FrameworkElement element) ApplyClip(element);
        }

        private static void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element) ApplyClip(element);
        }

        private static void ApplyClip(FrameworkElement element)
        {
            var width = element.ActualWidth;
            var height = element.ActualHeight;
            if (width <= 0 || height <= 0) return;

            // Clamp the corner radius so it never exceeds half the smaller dimension.
            var cornerRadius = Math.Min(GetCornerRadius(element),
                Math.Min(width, height) / 2.0);
            var smoothing = GetCornerSmoothing(element);

            element.Clip = SmoothGeometry.Create(
                new Rect(0, 0, width, height),
                SmoothBorderRadius.All(cornerRadius, smoothing));
        }
    }
}