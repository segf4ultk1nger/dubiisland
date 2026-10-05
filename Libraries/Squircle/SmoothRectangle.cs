using System.Windows;
using System.Windows.Media;

namespace Squircle
{
    /// <summary>
    ///     A WPF element that draws a "squircle" (smooth-cornered rectangle) with optional
    ///     fill and stroke, supporting inside / center / outside stroke alignment.
    /// </summary>
    /// <remarks>
    ///     Renders via <see cref="OnRender" /> (like a <see cref="System.Windows.Shapes.Path" />)
    ///     so it can implement stroke alignment that WPF's <see cref="System.Windows.Shapes.Shape" />
    ///     does not support natively.
    /// </remarks>
    public class SmoothRectangle : FrameworkElement
    {
        public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(
            nameof(CornerRadius), typeof(double), typeof(SmoothRectangle),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty CornerSmoothingProperty = DependencyProperty.Register(
            nameof(CornerSmoothing), typeof(double), typeof(SmoothRectangle),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
            nameof(Fill), typeof(Brush), typeof(SmoothRectangle),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
            nameof(Stroke), typeof(Brush), typeof(SmoothRectangle),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
            nameof(StrokeThickness), typeof(double), typeof(SmoothRectangle),
            new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty StrokeAlignProperty = DependencyProperty.Register(
            nameof(StrokeAlign), typeof(BorderAlign), typeof(SmoothRectangle),
            new FrameworkPropertyMetadata(BorderAlign.Center, FrameworkPropertyMetadataOptions.AffectsRender));

        public double CornerRadius
        {
            get => (double)GetValue(CornerRadiusProperty);
            set => SetValue(CornerRadiusProperty, value);
        }

        public double CornerSmoothing
        {
            get => (double)GetValue(CornerSmoothingProperty);
            set => SetValue(CornerSmoothingProperty, value);
        }

        public Brush? Fill
        {
            get => (Brush?)GetValue(FillProperty);
            set => SetValue(FillProperty, value);
        }

        public Brush? Stroke
        {
            get => (Brush?)GetValue(StrokeProperty);
            set => SetValue(StrokeProperty, value);
        }

        public double StrokeThickness
        {
            get => (double)GetValue(StrokeThicknessProperty);
            set => SetValue(StrokeThicknessProperty, value);
        }

        public BorderAlign StrokeAlign
        {
            get => (BorderAlign)GetValue(StrokeAlignProperty);
            set => SetValue(StrokeAlignProperty, value);
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            var rect = new Rect(RenderSize);
            if (rect.IsEmpty) return;

            var radius = SmoothBorderRadius.All(CornerRadius, CornerSmoothing);
            var border = new SmoothRectangleBorder(
                new SmoothBorderSide(StrokeThickness, Stroke),
                radius,
                StrokeAlign);

            var geometry = border.GetOuterGeometry(rect);
            // A (possibly transparent) fill keeps the region hit-testable.
            drawingContext.DrawGeometry(Fill ?? Brushes.Transparent, null, geometry);

            if (Stroke != null && StrokeThickness > 0) border.Paint(drawingContext, rect);
        }
    }
}