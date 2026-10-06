using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Models.Components;
using ClassIsland.Core.Models.Theming;
using ClassIsland.Models;
using ClassIsland.Services;
using ClassIsland.Services.Scripting;

namespace ClassIsland.Controls.Island;

/// <summary>
/// 只读的主界面岛预览。复用 <see cref="IslandRenderer"/> 的布局与绘制，实时跟随当前组件配置变化，
/// 供组件设置页在编辑时确认外观。以 <see cref="OnRender"/> 绘制，适配普通 WPF 视觉树（非 HwndSource）。
/// </summary>
public sealed class IslandPreview : FrameworkElement
{
    private SettingsService? _settingsService;
    private IThemeService? _themeService;
    private IComponentsService? _componentsService;
    private IslandContext? _context;
    private IslandRenderer? _renderer;
    private bool _initialized;
    private bool _hooked;

    public void Initialize(
        SettingsService settingsService,
        IThemeService themeService,
        IComponentsService componentsService,
        ILessonsService lessonsService,
        IProfileService profileService,
        IExactTimeService exactTimeService,
        ScriptConditionEvaluator conditionEvaluator,
        IWeatherService weatherService)
    {
        if (_initialized)
            return;
        _initialized = true;

        _settingsService = settingsService;
        _themeService = themeService;
        _componentsService = componentsService;

        _context = new IslandContext(settingsService.Settings, lessonsService, profileService, exactTimeService,
            conditionEvaluator, weatherService)
        {
            AccentColor = themeService.PrimaryColor,
            ComponentBounds = new Dictionary<ComponentSettings, Rect>()
        };
        _renderer = new IslandRenderer(_context);
        _renderer.Invalidated += OnRendererInvalidated;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnRendererInvalidated(object? sender, EventArgs e)
    {
        _layoutDirty = true;
        InvalidateMeasure();
        InvalidateVisual();
    }

    /// <summary>预览内容（岛）的设计尺寸（未乘缩放）。</summary>
    public Size ContentSize { get; private set; }

    /// <summary>取组件在内容自然坐标中的槽位矩形。</summary>
    public bool TryGetComponentBounds(ComponentSettings component, out Rect bounds)
    {
        if (_renderer == null)
        {
            bounds = Rect.Empty;
            return false;
        }

        return _renderer.TryGetComponentBounds(component, out bounds);
    }

    /// <summary>取整行在内容自然坐标中的矩形。</summary>
    public bool TryGetLineBounds(int lineNumber, out Rect bounds)
    {
        if (_renderer == null)
        {
            bounds = Rect.Empty;
            return false;
        }

        return _renderer.TryGetLineBounds(lineNumber, out bounds);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_initialized || _hooked)
            return;
        _hooked = true;
        _context!.PixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        HookComponentCollections();
        BuildComponents();
        RefreshTheme();
        _componentsService!.PropertyChanged += OnComponentsServicePropertyChanged;
        _settingsService!.Settings.PropertyChanged += OnSettingsChanged;
        _themeService!.ThemeUpdated += OnThemeUpdated;
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (!_hooked)
            return;
        _hooked = false;
        UnhookComponentCollections();
        _componentsService!.PropertyChanged -= OnComponentsServicePropertyChanged;
        _settingsService!.Settings.PropertyChanged -= OnSettingsChanged;
        _themeService!.ThemeUpdated -= OnThemeUpdated;
        _renderer!.Clear();
    }

    private static readonly DependencyPropertyKey PreviewContentCapKey =
        DependencyProperty.RegisterReadOnly(nameof(PreviewContentCap), typeof(double), typeof(IslandPreview),
            new FrameworkPropertyMetadata(double.PositiveInfinity));

    /// <summary>预览内容的最大高度：2 条岛在原样（100%）下的高度。超出则整体等比缩小。</summary>
    public static readonly DependencyProperty PreviewContentCapProperty = PreviewContentCapKey.DependencyProperty;

    public double PreviewContentCap => (double)GetValue(PreviewContentCapProperty);

    private bool _layoutDirty = true;
    private Size _naturalSize;
    private double _measuredWidth = -1;

    protected override Size MeasureOverride(Size availableSize)
    {
        if (_renderer == null)
            return new Size(0, 0);
        var width = double.IsInfinity(availableSize.Width) ? 960 : availableSize.Width;
        var scale = _renderer.Scale;
        // 只在布局失效或可用宽度变化时重建组件树。否则每次重绘（选中/高亮）都会重新 Build 全部组件，
        // 课程表这类组件重建成本很高，会明显卡顿。
        if (_layoutDirty || Math.Abs(width - _measuredWidth) > 0.01)
        {
            _measuredWidth = width;
            _naturalSize = _renderer.Measure(new Size(width / scale, double.PositiveInfinity));
            _layoutDirty = false;
            ContentSize = _naturalSize;
            var cap = _renderer.GetLinesBottom(2) * scale;
            if (Math.Abs(cap - PreviewContentCap) > 0.01)
            {
                SetValue(PreviewContentCapKey, cap);
            }
        }

        return new Size(_naturalSize.Width * scale, _naturalSize.Height * scale);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (_renderer == null || _naturalSize.Width <= 0 || _naturalSize.Height <= 0)
            return;
        var scale = _renderer.Scale;
        var contentWidth = _naturalSize.Width * scale;
        var contentHeight = _naturalSize.Height * scale;
        var x = Math.Max(0, (RenderSize.Width - contentWidth) / 2);
        var y = Math.Max(0, (RenderSize.Height - contentHeight) / 2);

        drawingContext.PushTransform(new ScaleTransform(scale, scale));
        drawingContext.PushTransform(new TranslateTransform(x / scale, y / scale));
        _renderer.Render(drawingContext, new Rect(_naturalSize));
        drawingContext.Pop();
        drawingContext.Pop();
    }

    /// <summary>组件树重建后触发（此时旧槽位坐标已失效，外层应等下一帧重算高亮/聚焦）。</summary>
    public event EventHandler? ContentChanged;

    /// <summary>按组件服务的当前配置重建整个组件树。</summary>
    private void BuildComponents()
    {
        _renderer!.Clear();
        var lines = _componentsService!.CurrentComponents.Lines;
        for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            foreach (var settings in lines[lineIndex].Children)
            {
                var component = IslandComponentFactory.Create(settings, _context!);
                if (component == null)
                    continue;
                component.LineNumber = lineIndex;
                _renderer.Add(component);
            }
        }

        ContentChanged?.Invoke(this, EventArgs.Empty);
    }

    private void HookComponentCollections()
    {
        var lines = _componentsService!.CurrentComponents.Lines;
        lines.CollectionChanged -= OnLinesChanged;
        lines.CollectionChanged += OnLinesChanged;
        foreach (var line in lines)
        {
            line.Children.CollectionChanged -= OnChildrenChanged;
            line.Children.CollectionChanged += OnChildrenChanged;
        }
    }

    private void UnhookComponentCollections()
    {
        var lines = _componentsService!.CurrentComponents.Lines;
        lines.CollectionChanged -= OnLinesChanged;
        foreach (var line in lines)
        {
            line.Children.CollectionChanged -= OnChildrenChanged;
        }
    }

    private void OnLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        HookComponentCollections();
        RequestBuildComponents();
    }

    private void OnChildrenChanged(object? sender, NotifyCollectionChangedEventArgs e) => RequestBuildComponents();

    private void OnComponentsServicePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IComponentsService.CurrentComponents))
        {
            UnhookComponentCollections();
            HookComponentCollections();
            RequestBuildComponents();
        }
    }

    private bool _rebuildScheduled;

    /// <summary>
    /// 合并同一轮操作内的多次集合变更，只重建一次。跨集合移动是「移除 + 插入」两次变更，
    /// 若每次都同步重建，中间态会在渲染线程上画出来（界面看起来“分两步”）。
    /// </summary>
    private void RequestBuildComponents()
    {
        if (_rebuildScheduled)
            return;
        _rebuildScheduled = true;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            _rebuildScheduled = false;
            BuildComponents();
        }), DispatcherPriority.Render);
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e) => RefreshTheme();

    private void OnThemeUpdated(object? sender, ThemeUpdatedEventArgs e) => RefreshTheme();

    /// <summary>把主题色、前景色、主题背景色同步给渲染上下文，并重建画笔。</summary>
    private void RefreshTheme()
    {
        var settings = _settingsService!.Settings;
        _context!.InvalidateFont();
        _context.AccentColor = _themeService!.PrimaryColor;
        _context.ForegroundColor = settings.IsCustomForegroundColorEnabled
            ? settings.CustomForegroundColor
            : ResolveThemeColor("MahApps.Brushes.ThemeForeground", Colors.White);
        _context.ThemeBackground = ResolveThemeColor("MahApps.Brushes.ThemeBackground", Color.FromRgb(0x1F, 0x1F, 0x1F));
        _renderer!.RefreshStyles();
        _renderer.Invalidate();
    }

    private static Color ResolveThemeColor(string key, Color fallback)
        => Application.Current?.TryFindResource(key) is SolidColorBrush brush ? brush.Color : fallback;
}
