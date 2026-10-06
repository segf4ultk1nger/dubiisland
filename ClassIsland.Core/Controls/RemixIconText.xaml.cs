using System.Windows;
using System.Windows.Controls;
using MahApps.Metro.IconPacks;

namespace ClassIsland.Core.Controls;

/// <summary>
/// RemixIcon 版本的 IconText：图标 + 可选文本。
/// </summary>
public partial class RemixIconText : UserControl
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(RemixIconText), new PropertyMetadata(default(string)));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(PackIconRemixIconKind), typeof(RemixIconText),
        new PropertyMetadata(default(PackIconRemixIconKind)));

    public PackIconRemixIconKind Kind
    {
        get => (PackIconRemixIconKind)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public static readonly DependencyProperty IconSizeProperty = DependencyProperty.Register(
        nameof(IconSize), typeof(double), typeof(RemixIconText), new PropertyMetadata(16d));

    public double IconSize
    {
        get => (double)GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    public static readonly DependencyProperty IconMarginProperty = DependencyProperty.Register(
        nameof(IconMargin), typeof(Thickness), typeof(RemixIconText),
        new PropertyMetadata(new Thickness(6, 0, 0, 0)));

    public Thickness IconMargin
    {
        get => (Thickness)GetValue(IconMarginProperty);
        set => SetValue(IconMarginProperty, value);
    }

    public RemixIconText()
    {
        InitializeComponent();
    }
}
