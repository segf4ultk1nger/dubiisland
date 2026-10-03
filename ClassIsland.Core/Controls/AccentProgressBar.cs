using System.Windows;
using System.Windows.Controls;

namespace ClassIsland.Core.Controls;

/// <summary>
/// 带前景缩放的 <see cref="ProgressBar"/>。
/// </summary>
public class AccentProgressBar : ProgressBar
{
    public static readonly DependencyProperty ForegroundScaleProperty = DependencyProperty.Register(
        nameof(ForegroundScale), typeof(double), typeof(AccentProgressBar), new PropertyMetadata(default(double)));

    public double ForegroundScale
    {
        get { return (double)GetValue(ForegroundScaleProperty); }
        set { SetValue(ForegroundScaleProperty, value); }
    }
}