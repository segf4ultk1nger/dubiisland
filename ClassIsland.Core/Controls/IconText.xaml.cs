using System.Windows;
using System.Windows.Controls;

namespace ClassIsland.Core.Controls;

/// <summary>
/// IconText.xaml 的交互逻辑
/// </summary>
public partial class IconText : UserControl
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(IconText), new PropertyMetadata(default(string)));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(string), typeof(IconText), new PropertyMetadata(default(string)));

    public string Kind
    {
        get => (string)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public static readonly DependencyProperty IconSizeProperty = DependencyProperty.Register(
        nameof(IconSize), typeof(double), typeof(IconText), new PropertyMetadata(16d, null, CoerceIconSize));

    static object CoerceIconSize(DependencyObject d, object baseValue) =>
        baseValue is double size && size > 0 && !double.IsNaN(size) && !double.IsInfinity(size) ? size : 16d;

    /// <summary>
    /// Glyph size in pixels. Matches the old Pack icon width/height, and defaults to 16.
    /// </summary>
    public double IconSize
    {
        get => (double)GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    public static readonly DependencyProperty IconMarginProperty = DependencyProperty.Register(
        nameof(IconMargin), typeof(Thickness), typeof(IconText), new PropertyMetadata(new Thickness(6, 0, 0, 0)));

    public Thickness IconMargin
    {
        get => (Thickness)GetValue(IconMarginProperty);
        set => SetValue(IconMarginProperty, value);
    }

    public IconText()
    {
        InitializeComponent();
    }
}