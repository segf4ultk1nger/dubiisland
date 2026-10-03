using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace ClassIsland.Core.Controls.IconControl;

/// <summary>
/// 图标控件。用于显示位图图标或MD图标包图标，并支持在图标不可用时显示指定的默认图标。
/// </summary>
[TemplatePart(Name = "PART_ImageIcon", Type = typeof(Image))]
public class IconControl : Control
{
    #region Props

    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph), typeof(string), typeof(IconControl), new PropertyMetadata(default(string)));

    public string Glyph
    {
        get { return (string)GetValue(GlyphProperty); }
        set { SetValue(GlyphProperty, value); }
    }

    public static readonly DependencyProperty ImageSourceProperty = DependencyProperty.Register(
        nameof(ImageSource), typeof(ImageSource), typeof(IconControl), new PropertyMetadata(default(ImageSource),
            (o, args) =>
            {
                if (o is IconControl control)
                {
                    control.OnImageSourceChanged(o, args);
                }
            }));

    public ImageSource ImageSource
    {
        get { return (ImageSource)GetValue(ImageSourceProperty); }
        set { SetValue(ImageSourceProperty, value); }
    }

    public static readonly DependencyProperty IconKindProperty = DependencyProperty.Register(
        nameof(IconKind), typeof(IconControlIconKind), typeof(IconControl), new PropertyMetadata(default(IconControlIconKind), (o, args) =>
        {
            if (o is IconControl control)
            {
                control.OnIconKindChanged(o, args);
            }
        }));

    public IconControlIconKind IconKind
    {
        get { return (IconControlIconKind)GetValue(IconKindProperty); }
        set { SetValue(IconKindProperty, value); }
    }

    public static readonly DependencyProperty FallbackGlyphProperty = DependencyProperty.Register(
        nameof(FallbackGlyph), typeof(string), typeof(IconControl), new PropertyMetadata(default(string)));

    public string FallbackGlyph
    {
        get { return (string)GetValue(FallbackGlyphProperty); }
        set { SetValue(FallbackGlyphProperty, value); }
    }

    public static readonly DependencyProperty RealIconKindProperty = DependencyProperty.Register(
        nameof(RealIconKind), typeof(IconControlIconKind), typeof(IconControl), new PropertyMetadata(IconControlIconKind.PackIcon));

    public IconControlIconKind RealIconKind
    {
        get { return (IconControlIconKind)GetValue(RealIconKindProperty); }
        set { SetValue(RealIconKindProperty, value); }
    }

    public static readonly DependencyProperty IsFallbackModeProperty = DependencyProperty.Register(
        nameof(IsFallbackMode), typeof(bool), typeof(IconControl), new PropertyMetadata(true));

    public bool IsFallbackMode
    {
        get { return (bool)GetValue(IsFallbackModeProperty); }
        set { SetValue(IsFallbackModeProperty, value); }
    }

    #endregion

    /// <inheritdoc />
    public override void OnApplyTemplate()
    {
        if (GetTemplateChild("PART_ImageIcon") is Image image)
        {
            image.ImageFailed += ImageOnImageFailed;
        }
        base.OnApplyTemplate();
    }

    private void ImageOnImageFailed(object? sender, ExceptionRoutedEventArgs e)
    {
        RealIconKind = IconControlIconKind.PackIcon;
        IsFallbackMode = true;
    }

    private void OnIconKindChanged(DependencyObject o, DependencyPropertyChangedEventArgs args)
    {
        UpdateIconSource();
    }

    private void UpdateIconSource()
    {
        Console.WriteLine(ImageSource);
        if (IconKind == IconControlIconKind.Image && ImageSource == null)
        {
            RealIconKind = IconControlIconKind.PackIcon;
            IsFallbackMode = true;
            return;
        }
        RealIconKind = IconKind;
        IsFallbackMode = false;
    }

    private void OnImageSourceChanged(DependencyObject o, DependencyPropertyChangedEventArgs args)
    {
        UpdateIconSource();
    }

    static IconControl()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(IconControl), new FrameworkPropertyMetadata(typeof(IconControl)));
    }
}