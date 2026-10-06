using System;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ClassIsland.Controls;

/// <summary>搜索命中后，在目标元素上闪一下红色直角矩形线框。</summary>
internal sealed class SearchHighlightAdorner : Adorner
{
    private static readonly Pen HighlightPen = CreatePen();

    private SearchHighlightAdorner(UIElement adornedElement) : base(adornedElement)
    {
        IsHitTestVisible = false;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var rect = new Rect(AdornedElement.RenderSize);
        rect.Inflate(1, 1);
        drawingContext.DrawRectangle(null, HighlightPen, rect);
    }

    /// <summary>在元素上播放一次闪烁，结束后自动移除。</summary>
    public static void Flash(UIElement element)
    {
        var layer = AdornerLayer.GetAdornerLayer(element);
        if (layer == null)
        {
            return;
        }

        var adorner = new SearchHighlightAdorner(element) { Opacity = 0 };
        layer.Add(adorner);

        var animation = new DoubleAnimationUsingKeyFrames();
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(80))));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(360))));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(640))));

        var storyboard = new Storyboard();
        Storyboard.SetTarget(animation, adorner);
        Storyboard.SetTargetProperty(animation, new PropertyPath(OpacityProperty));
        storyboard.Children.Add(animation);
        storyboard.Completed += (_, _) => layer.Remove(adorner);
        storyboard.Begin();
    }

    private static Pen CreatePen()
    {
        var brush = new SolidColorBrush(Color.FromRgb(0xFF, 0x3B, 0x30));
        brush.Freeze();
        var pen = new Pen(brush, 2);
        pen.Freeze();
        return pen;
    }
}
