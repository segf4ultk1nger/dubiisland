using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Squircle
{
    /// <summary>
    ///     A <see cref="Border" /> that draws a smooth-cornered background and clips its
    ///     <see cref="Border.Child" /> to a squircle shape.
    /// </summary>
    public class SmoothBorder : Border
    {
        public new static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(
            nameof(CornerRadius), typeof(double), typeof(SmoothBorder),
            new FrameworkPropertyMetadata(0.0,
                FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsArrange,
                OnClipAffectingPropertyChanged));

        public static readonly DependencyProperty CornerSmoothingProperty = DependencyProperty.Register(
            nameof(CornerSmoothing), typeof(double), typeof(SmoothBorder),
            new FrameworkPropertyMetadata(0.0,
                FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsArrange,
                OnClipAffectingPropertyChanged));

        public static readonly DependencyProperty ClipToSmoothRectProperty = DependencyProperty.Register(
            nameof(ClipToSmoothRect), typeof(bool), typeof(SmoothBorder),
            new FrameworkPropertyMetadata(true,
                FrameworkPropertyMetadataOptions.AffectsArrange,
                OnClipAffectingPropertyChanged));

        public new double CornerRadius
        {
            get => (double)GetValue(CornerRadiusProperty);
            set => SetValue(CornerRadiusProperty, value);
        }

        public double CornerSmoothing
        {
            get => (double)GetValue(CornerSmoothingProperty);
            set => SetValue(CornerSmoothingProperty, value);
        }

        /// <summary>Whether the child should be clipped to the smooth rect. Defaults to true.</summary>
        public bool ClipToSmoothRect
        {
            get => (bool)GetValue(ClipToSmoothRectProperty);
            set => SetValue(ClipToSmoothRectProperty, value);
        }

        private static void OnClipAffectingPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((SmoothBorder)d).UpdateChildClip();
        }

        protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);
            if (e.Property == BorderThicknessProperty || e.Property == PaddingProperty) UpdateChildClip();
        }

        protected override void OnVisualChildrenChanged(DependencyObject visualAdded, DependencyObject visualRemoved)
        {
            base.OnVisualChildrenChanged(visualAdded, visualRemoved);
            UpdateChildClip();
        }

        protected override Size ArrangeOverride(Size arrangeSize)
        {
            var size = base.ArrangeOverride(arrangeSize);
            UpdateChildClip(size);
            return size;
        }

        private void UpdateChildClip()
        {
            UpdateChildClip(RenderSize);
        }

        private void UpdateChildClip(Size size)
        {
            if (Child == null || size.Width <= 0 || size.Height <= 0) return;

            if (ClipToSmoothRect)
            {
                // Clip in the child's OWN local coordinate space: origin (0,0), sized to the
                // child's content area. The corner radius is pulled in by half the border
                // thickness so the inner clip hugs the outer border outline.
                var childWidth = Math.Max(0,
                    size.Width - BorderThickness.Left - BorderThickness.Right - Padding.Left - Padding.Right);
                var childHeight = Math.Max(0,
                    size.Height - BorderThickness.Top - BorderThickness.Bottom - Padding.Top - Padding.Bottom);

                if (childWidth > 0 && childHeight > 0)
                {
                    var padX = (BorderThickness.Left + BorderThickness.Right) / 2.0;
                    var innerRadius = Math.Max(0, CornerRadius - padX);

                    Child.Clip = SmoothGeometry.Create(
                        new Rect(0, 0, childWidth, childHeight),
                        SmoothBorderRadius.All(innerRadius, CornerSmoothing));
                }
                else
                {
                    Child.Clip = null;
                }
            }
            else
            {
                Child.Clip = null;
            }
        }

        protected override void OnRender(DrawingContext dc)
        {
            var rect = new Rect(RenderSize);
            if (rect.IsEmpty) return;

            if (Background != null || BorderBrush != null)
            {
                var radius = SmoothBorderRadius.All(CornerRadius, CornerSmoothing);
                var geometry = radius.ToGeometry(rect);

                if (Background != null) dc.DrawGeometry(Background, null, geometry);

                if (BorderBrush != null && BorderThickness != default)
                {
                    var pen = new Pen(BorderBrush, Math.Max(BorderThickness.Top, Math.Max(
                        BorderThickness.Left, Math.Max(BorderThickness.Right, BorderThickness.Bottom))));
                    pen.Freeze();
                    dc.DrawGeometry(null, pen, geometry);
                }
            }
            else
            {
                base.OnRender(dc);
            }
        }
    }
}