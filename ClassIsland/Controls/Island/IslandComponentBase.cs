using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using ClassIsland.Core.Models.Components;

namespace ClassIsland.Controls.Island;

/// <summary>
/// 自绘组件基类。负责把 <see cref="ComponentSettings"/> 的外观设置（字号、前景色、宽高约束、对齐、
/// 隐藏规则、外边距）统一应用到一个自绘组件上；子类只需实现“内容”的测量与绘制。
/// </summary>
public abstract class IslandComponentBase : IIslandComponent
{
    /// <summary>组件在行内的左右外边距（等价 ComponentPresenter 的 Margin="6 0"）。</summary>
    public const double HorizontalMargin = 6;

    protected IslandComponentBase(ComponentSettings component)
    {
        Component = component;
        component.PropertyChanged += OnComponentPropertyChanged;
        if (component.Children != null)
            component.Children.CollectionChanged += OnChildrenCollectionChanged;
    }

    protected ComponentSettings Component { get; }

    protected IslandContext Context { get; private set; } = null!;

    /// <summary>上一次测量得到的内容宽度（不含外边距）。</summary>
    protected double ContentWidth { get; private set; }

    private bool _hidByRule;
    private Brush? _foregroundBrush;
    private Color _foregroundColor;
    private bool _foregroundCustom;

    public int LineNumber { get; set; }

    public virtual bool IsVisible => Component.IsVisible && !_hidByRule;

    public event EventHandler? Invalidated;

    public Size Measure(Size availableSize, IslandContext context)
    {
        Context = context;
        _hidByRule = Component.HideOnRule && context.RulesetService.IsRulesetSatisfied(Component.HidingRules);
        if (!IsVisible)
        {
            ContentWidth = 0;
            return new Size(0, 0);
        }

        var inner = new Size(Math.Max(0, availableSize.Width - HorizontalMargin * 2), availableSize.Height);
        var content = MeasureContent(inner, context);
        ContentWidth = content.Width;
        return new Size(ResolveOuterWidth(content.Width) + HorizontalMargin * 2, content.Height);
    }

    public void Render(DrawingContext drawingContext, Rect slot, IslandContext context)
    {
        Context = context;
        var available = Math.Max(0, slot.Width - HorizontalMargin * 2);
        var contentWidth = Math.Min(ContentWidth, available);
        var slack = available - contentWidth;
        var offset = Component.HorizontalAlignment switch
        {
            HorizontalAlignment.Center => slack / 2,
            HorizontalAlignment.Right => slack,
            _ => 0
        };
        RenderContent(drawingContext,
            new Rect(slot.X + HorizontalMargin + offset, slot.Y, contentWidth, slot.Height), context);

        if (ReferenceEquals(Component, context.HighlightedComponent))
        {
            drawingContext.DrawRectangle(null, HighlightPen, slot);
        }
    }

    private static readonly Pen HighlightPen = CreateHighlightPen();

    private static Pen CreateHighlightPen()
    {
        var pen = new Pen(Frozen(Color.FromRgb(0xFF, 0xC4, 0x00)), 2);
        pen.Freeze();
        return pen;
    }

    protected double ResolveOuterWidth(double content)
    {
        if (Component.IsFixedWidthEnabled)
            return Component.FixedWidth;
        if (Component.IsMinWidthEnabled)
            content = Math.Max(content, Component.MinWidth);
        if (Component.IsMaxWidthEnabled)
            content = Math.Min(content, Component.MaxWidth);
        return content;
    }

    protected double SecondaryFontSize => ResolveFontSize(Component.MainWindowSecondaryFontSize,
        Context.Settings.MainWindowSecondaryFontSize);

    protected double BodyFontSize => ResolveFontSize(Component.MainWindowBodyFontSize,
        Context.Settings.MainWindowBodyFontSize);

    protected double EmphasizedFontSize => ResolveFontSize(Component.MainWindowEmphasizedFontSize,
        Context.Settings.MainWindowEmphasizedFontSize);

    protected double LargeFontSize => ResolveFontSize(Component.MainWindowLargeFontSize,
        Context.Settings.MainWindowLargeFontSize);

    private double ResolveFontSize(double local, double global)
        => Component.IsResourceOverridingEnabled ? local : global;

    /// <summary>组件有效前景色：启用自定义时用组件色，否则用主题前景色。</summary>
    protected Color EffectiveForegroundColor
        => Component.IsCustomForegroundColorEnabled ? Component.ForegroundColor : Context.ForegroundColor;

    /// <summary>组件有效前景色：启用自定义时用组件色，否则用主题前景色。</summary>
    protected Brush ForegroundBrush
    {
        get
        {
            var custom = Component.IsCustomForegroundColorEnabled;
            var color = EffectiveForegroundColor;
            if (_foregroundBrush == null || _foregroundCustom != custom || _foregroundColor != color)
            {
                _foregroundCustom = custom;
                _foregroundColor = color;
                _foregroundBrush = Frozen(color);
            }

            return _foregroundBrush;
        }
    }

    protected abstract Size MeasureContent(Size availableSize, IslandContext context);

    protected virtual void RenderContent(DrawingContext drawingContext, Rect slot, IslandContext context)
    {
    }

    protected FormattedText MakeText(string text, double size, Brush brush, FontWeight? weight = null)
        => new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(Context.FontFamily, FontStyles.Normal, weight ?? FontWeights.Normal, FontStretches.Normal),
            size, brush, Context.PixelsPerDip);

    protected FormattedText MakeIcon(string glyph, double size, Brush brush)
        => new(glyph, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(RemixIconGlyph.FontFamily), size, brush, Context.PixelsPerDip);

    protected static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    protected void Invalidate() => Invalidated?.Invoke(this, EventArgs.Empty);

    private void OnComponentPropertyChanged(object? sender, PropertyChangedEventArgs e) => Invalidate();

    protected virtual void OnChildrenCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Invalidate();
}
