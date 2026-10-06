using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Models.Components;
using ClassIsland.Services;
using ClassIsland.Shared;
using FormsScreen = System.Windows.Forms.Screen;
using SettingsModel = ClassIsland.Models.Settings;

namespace ClassIsland.Controls.Island;

/// <summary>
/// 通用岛预览控件：置顶只读预览，支持背景（纯色/壁纸）、停靠对齐、选中聚焦与选中/悬停高亮。
/// 通过 <see cref="SelectedNode"/> / <see cref="HoveredNode"/> 接收要指示的组件或行。
/// </summary>
public partial class IslandPreviewControl : UserControl
{
    public static readonly DependencyProperty SelectedNodeProperty = DependencyProperty.Register(
        nameof(SelectedNode), typeof(object), typeof(IslandPreviewControl),
        new PropertyMetadata(null, (o, _) =>
        {
            if (o is IslandPreviewControl control)
            {
                control.UpdatePreviewHighlight();
                control.UpdatePreviewFocus();
            }
        }));

    /// <summary>当前选中的节点（<see cref="ComponentSettings"/> 或 <see cref="MainWindowLineSettings"/>）。</summary>
    public object? SelectedNode
    {
        get => GetValue(SelectedNodeProperty);
        set => SetValue(SelectedNodeProperty, value);
    }

    public static readonly DependencyProperty HoveredNodeProperty = DependencyProperty.Register(
        nameof(HoveredNode), typeof(object), typeof(IslandPreviewControl),
        new PropertyMetadata(null, (o, _) => (o as IslandPreviewControl)?.UpdateHoverHighlight()));

    /// <summary>当前鼠标悬停的节点（用于蓝框实时指示）。</summary>
    public object? HoveredNode
    {
        get => GetValue(HoveredNodeProperty);
        set => SetValue(HoveredNodeProperty, value);
    }

    public static readonly DependencyProperty IsCompactProperty = DependencyProperty.Register(
        nameof(IsCompact), typeof(bool), typeof(IslandPreviewControl), new PropertyMetadata(false));

    /// <summary>紧凑模式：选中高亮不做过渡动画。</summary>
    public bool IsCompact
    {
        get => (bool)GetValue(IsCompactProperty);
        set => SetValue(IsCompactProperty, value);
    }

    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private readonly RectangleGeometry _highlightGeometry = new();
    private readonly RectangleGeometry _hoverGeometry = new();
    private bool _highlightVisible;
    private BitmapCache? _focusCache;
    private int _focusAnimationToken;
    private bool _hasFocusTarget;
    private double _focusTargetScale = 1;
    private double _focusTargetX;
    private double _focusTargetY;

    private readonly SettingsService? _settingsService;
    private readonly WallpaperPickingService? _wallpaperService;
    private readonly IComponentsService? _componentsService;

    public IslandPreviewControl()
    {
        InitializeComponent();
        SelectionHighlight.Data = _highlightGeometry;
        HoverHighlight.Data = _hoverGeometry;

        // 必须在 IslandPreviewHost 自身的 Loaded 之前完成 Initialize，因此放在构造函数里。
        _settingsService = IAppHost.GetService<SettingsService>();
        _componentsService = IAppHost.GetService<IComponentsService>();
        _wallpaperService = IAppHost.GetService<WallpaperPickingService>();
        IslandPreviewHost.Initialize(
            _settingsService,
            IAppHost.GetService<IThemeService>(),
            _componentsService,
            IAppHost.GetService<ILessonsService>(),
            IAppHost.GetService<IProfileService>(),
            IAppHost.GetService<IExactTimeService>(),
            IAppHost.GetService<IRulesetService>(),
            IAppHost.GetService<IWeatherService>());

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_settingsService != null)
        {
            _settingsService.Settings.PropertyChanged -= OnSettingsChanged;
            _settingsService.Settings.PropertyChanged += OnSettingsChanged;
        }

        if (_wallpaperService != null)
        {
            _wallpaperService.PropertyChanged -= OnWallpaperChanged;
            _wallpaperService.PropertyChanged += OnWallpaperChanged;
        }

        UpdateIslandAlignment();
        UpdatePreviewBackground();
        UpdatePreviewHighlight();
        UpdatePreviewFocus();
        UpdateHoverHighlight();
        if (_settingsService?.Settings.ComponentPreviewBackgroundMode == 3)
            _ = _wallpaperService?.GetWallpaperAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_settingsService != null)
            _settingsService.Settings.PropertyChanged -= OnSettingsChanged;
        if (_wallpaperService != null)
            _wallpaperService.PropertyChanged -= OnWallpaperChanged;
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs args)
    {
        switch (args.PropertyName)
        {
            case nameof(SettingsModel.WindowDockingLocation):
            case nameof(SettingsModel.WindowDockingMonitorIndex):
            case nameof(SettingsModel.WindowDockingOffsetX):
            case nameof(SettingsModel.WindowDockingOffsetY):
            case nameof(SettingsModel.IsIgnoreWorkAreaEnabled):
            case nameof(SettingsModel.Scale):
                UpdateIslandAlignment();
                UpdateWallpaperBackgroundIfNeeded();
                UpdatePreviewFocus();
                UpdatePreviewHighlight(false);
                break;
            case nameof(SettingsModel.ComponentPreviewBackgroundMode):
                if (_settingsService?.Settings.ComponentPreviewBackgroundMode == 3)
                    _ = _wallpaperService?.GetWallpaperAsync();
                UpdatePreviewBackground();
                break;
            case nameof(SettingsModel.IsComponentPreviewHighlightEnabled):
                UpdatePreviewHighlight();
                break;
            case nameof(SettingsModel.IsComponentPreviewFocusAnimationEnabled):
                UpdatePreviewFocus();
                break;
        }
    }

    private void OnWallpaperChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WallpaperPickingService.WallpaperImage))
        {
            // 壁纸在后台线程提取，WallpaperImage 可能在非 UI 线程变更。
            Dispatcher.InvokeAsync(UpdateWallpaperBackgroundIfNeeded);
        }
    }

    private void PreviewBorder_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateWallpaperBackgroundIfNeeded();
        UpdatePreviewFocus();
        UpdatePreviewHighlight(false);
        UpdateHoverHighlight();
    }

    private void UpdatePreviewBackground()
    {
        switch (_settingsService?.Settings.ComponentPreviewBackgroundMode)
        {
            case 0:
                PreviewRoot.Background = Brushes.Black;
                PreviewWallpaperLayer.Background = null;
                break;
            case 2:
                PreviewRoot.Background = Brushes.White;
                PreviewWallpaperLayer.Background = null;
                break;
            case 3:
                // 壁纸贴边后可能有留白（顶部/底部/两侧），用黑色兜底。
                PreviewRoot.Background = Brushes.Black;
                PreviewWallpaperLayer.Background = BuildWallpaperBrush();
                break;
            default:
                PreviewRoot.Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));
                PreviewWallpaperLayer.Background = null;
                break;
        }
    }

    private void UpdateWallpaperBackgroundIfNeeded()
    {
        if (_settingsService?.Settings.ComponentPreviewBackgroundMode == 3)
        {
            UpdatePreviewBackground();
        }
    }

    /// <summary>预览里的岛按停靠位置贴左上/中/右下，而不是永远居中。</summary>
    private void UpdateIslandAlignment()
    {
        if (_settingsService == null)
            return;
        var location = _settingsService.Settings.WindowDockingLocation;
        IslandViewbox.HorizontalAlignment = location switch
        {
            0 or 3 => HorizontalAlignment.Left,
            1 or 4 => HorizontalAlignment.Center,
            _ => HorizontalAlignment.Right
        };
        IslandViewbox.VerticalAlignment = location is 3 or 4 or 5
            ? VerticalAlignment.Bottom
            : VerticalAlignment.Top;
    }

    /// <summary>
    /// 取景区间 = 预览区大小的一块矩形，锚点贴在岛停靠的角/边上（即把预览框「叠」在岛的位置），
    /// 因此壁纸铺满整个预览，且随岛的位置/大小变化，而不随设置窗口移动。
    /// </summary>
    private Brush? BuildWallpaperBrush()
    {
        var image = _wallpaperService?.WallpaperImage;
        var content = IslandPreviewHost.ContentSize;
        if (image == null || image.PixelWidth <= 0 || content.Width <= 0 ||
            PreviewBorder.ActualWidth <= 0 || PreviewBorder.ActualHeight <= 0 || _settingsService == null)
        {
            return null;
        }

        var settings = _settingsService.Settings;
        var scale = settings.Scale <= 0 ? 1.0 : settings.Scale;
        var dpi = VisualTreeHelper.GetDpi(PreviewBorder);
        var dpiX = dpi.DpiScaleX;
        var dpiY = dpi.DpiScaleY;

        var index = settings.WindowDockingMonitorIndex;
        var screens = FormsScreen.AllScreens;
        var screen = index >= 0 && index < screens.Length ? screens[index] : FormsScreen.PrimaryScreen;
        if (screen == null)
        {
            return null;
        }

        var ignoreWorkArea = settings.IsIgnoreWorkAreaEnabled;
        var areaTop = ignoreWorkArea ? screen.Bounds.Top : screen.WorkingArea.Top;
        var areaBottom = ignoreWorkArea ? screen.Bounds.Bottom : screen.WorkingArea.Bottom;
        var areaLeft = ignoreWorkArea ? screen.Bounds.Left : screen.WorkingArea.Left;
        var areaWidth = ignoreWorkArea ? screen.Bounds.Width : screen.WorkingArea.Width;

        var location = settings.WindowDockingLocation;
        var hAlign = location switch
        {
            1 or 4 => 0.5,
            2 or 5 => 1.0,
            _ => 0.0
        };
        var vAlign = location is 3 or 4 or 5 ? 1.0 : 0.0;

        var islandW = content.Width * scale * dpiX;
        var islandH = content.Height * scale * dpiY;
        var islandLeft = areaLeft + settings.WindowDockingOffsetX + (areaWidth - islandW) * hAlign;
        var islandTop = location is 3 or 4 or 5
            ? areaBottom + settings.WindowDockingOffsetY - islandH
            : areaTop + settings.WindowDockingOffsetY;

        // Viewbox 的缩放系数：内容过高（超过 2 条岛高度）或过宽时会被 DownOnly 等比缩小，
        // 因此壁纸取景也要按同样比例缩放，才能反映岛上真实覆盖的壁纸区域。
        var desiredW = content.Width * scale;
        var desiredH = content.Height * scale;
        var s = 1.0;
        if (desiredW > 0.001 && IslandViewbox.ActualWidth > 0.001)
        {
            s = Math.Min(s, IslandViewbox.ActualWidth / desiredW);
        }
        if (desiredH > 0.001 && IslandViewbox.ActualHeight > 0.001)
        {
            s = Math.Min(s, IslandViewbox.ActualHeight / desiredH);
        }
        if (s <= 0.0001 || double.IsNaN(s) || double.IsInfinity(s))
        {
            s = 1.0;
        }

        // 岛在预览 Border 内相对左上角的偏移（按缩放后的实际尺寸），与 Viewbox 对齐方式一致。
        var padL = PreviewBorder.Padding.Left;
        var padT = PreviewBorder.Padding.Top;
        var innerW = Math.Max(0, PreviewBorder.ActualWidth - PreviewBorder.Padding.Left - PreviewBorder.Padding.Right);
        var innerH = Math.Max(0, PreviewBorder.ActualHeight - PreviewBorder.Padding.Top - PreviewBorder.Padding.Bottom);
        var renderedW = islandW / dpiX * s;
        var renderedH = islandH / dpiY * s;
        var offsetX = padL + (innerW - Math.Min(renderedW, innerW)) * hAlign;
        var offsetY = padT + (innerH - Math.Min(renderedH, innerH)) * vAlign;

        // 取景框 = 预览区大小 ÷ 缩放系数，再反向定位到岛在屏幕上的位置。
        var windowLeft = islandLeft - offsetX * dpiX / s;
        var windowTop = islandTop - offsetY * dpiY / s;
        var windowW = PreviewBorder.ActualWidth * dpiX / s;
        var windowH = PreviewBorder.ActualHeight * dpiY / s;

        var vx = GetSystemMetrics(SM_XVIRTUALSCREEN);
        var vy = GetSystemMetrics(SM_YVIRTUALSCREEN);
        var vw = Math.Max(1, GetSystemMetrics(SM_CXVIRTUALSCREEN));
        var vh = Math.Max(1, GetSystemMetrics(SM_CYVIRTUALSCREEN));
        var sx = (double)image.PixelWidth / vw;
        var sy = (double)image.PixelHeight / vh;

        var viewbox = new Rect((windowLeft - vx) * sx, (windowTop - vy) * sy, windowW * sx, windowH * sy);
        return new ImageBrush(image)
        {
            ViewboxUnits = BrushMappingMode.Absolute,
            Viewbox = viewbox,
            ViewportUnits = BrushMappingMode.RelativeToBoundingBox,
            Viewport = new Rect(0, 0, 1, 1),
            Stretch = Stretch.Fill,
            TileMode = TileMode.None
        };
    }

    private void UpdatePreviewHighlight(bool animate = true)
    {
        if (_settingsService?.Settings.IsComponentPreviewHighlightEnabled != true ||
            TryGetNodePreviewBounds(SelectedNode) is not { } bounds)
        {
            // 消失不做动画。
            _highlightGeometry.BeginAnimation(RectangleGeometry.RectProperty, null);
            SelectionHighlight.Visibility = Visibility.Collapsed;
            _highlightVisible = false;
            return;
        }

        // 出现不做动画；选中项之间过渡做动画（仅非紧凑模式）。
        if (_highlightVisible && animate && !IsCompact)
        {
            _highlightGeometry.BeginAnimation(RectangleGeometry.RectProperty,
                new RectAnimation(bounds, TimeSpan.FromMilliseconds(180))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
                });
        }
        else
        {
            _highlightGeometry.BeginAnimation(RectangleGeometry.RectProperty, null);
            _highlightGeometry.Rect = bounds;
        }

        SelectionHighlight.Visibility = Visibility.Visible;
        _highlightVisible = true;
    }

    /// <summary>预览里实时指示鼠标所在的组件/行（蓝框，无动画）。</summary>
    private void UpdateHoverHighlight()
    {
        if (TryGetNodePreviewBounds(HoveredNode) is not { } bounds)
        {
            HoverHighlight.Visibility = Visibility.Collapsed;
            return;
        }

        _hoverGeometry.Rect = bounds;
        HoverHighlight.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// 聚焦预览：把选中组件/行所在的画布区域平移到预览中心并放大（画布含岛与壁纸，作为整体变换）。
    /// 放大上限 150%，且保证选中元素放大后仍完整可见；未选中时带动画复位。
    /// </summary>
    private void UpdatePreviewFocus()
    {
        if (_settingsService?.Settings.IsComponentPreviewFocusAnimationEnabled != true ||
            TryGetNodePreviewBounds(SelectedNode) is not { } bounds ||
            PreviewCanvas.ActualWidth <= 0 || PreviewCanvas.ActualHeight <= 0)
        {
            AnimateFocus(1, 0, 0);
            return;
        }

        var w = PreviewCanvas.ActualWidth;
        var h = PreviewCanvas.ActualHeight;
        // 放大后仍完整可见：元素缩放到不超出预览区，且不超过 150%。
        var k = Math.Min(1.5, Math.Min(w / bounds.Width, h / bounds.Height));
        var centerX = bounds.X + bounds.Width / 2;
        var centerY = bounds.Y + bounds.Height / 2;
        // 变换 p -> k*p + t，使元素中心落到预览中心。
        AnimateFocus(k, w / 2 - k * centerX, h / 2 - k * centerY);
    }

    /// <summary>组件/行在 <see cref="PreviewCanvas"/> 坐标中的矩形（已含缩放与岛内偏移）。</summary>
    private Rect? TryGetNodePreviewBounds(object? node)
    {
        if (TryGetNodeBounds(node) is not { } r || r.Width <= 0 || r.Height <= 0 || _settingsService == null)
        {
            return null;
        }

        var scale = _settingsService.Settings.Scale <= 0 ? 1.0 : _settingsService.Settings.Scale;
        var contentW = IslandPreviewHost.ContentSize.Width * scale;
        var contentH = IslandPreviewHost.ContentSize.Height * scale;
        var offX = Math.Max(0, (IslandPreviewHost.ActualWidth - contentW) / 2);
        var offY = Math.Max(0, (IslandPreviewHost.ActualHeight - contentH) / 2);

        GeneralTransform toCanvas;
        try
        {
            toCanvas = IslandPreviewHost.TransformToAncestor(PreviewCanvas);
        }
        catch
        {
            return null;
        }

        var tl = toCanvas.Transform(new Point(r.Left * scale + offX, r.Top * scale + offY));
        var br = toCanvas.Transform(new Point(r.Right * scale + offX, r.Bottom * scale + offY));
        var bounds = new Rect(tl, br);
        return bounds.Width > 0 && bounds.Height > 0 ? bounds : null;
    }

    private Rect? TryGetNodeBounds(object? node)
    {
        if (node is ComponentSettings component &&
            IslandPreviewHost.TryGetComponentBounds(component, out var componentBounds))
        {
            return componentBounds;
        }

        if (node is MainWindowLineSettings line && _componentsService != null)
        {
            var index = _componentsService.CurrentComponents.Lines.IndexOf(line);
            if (index >= 0 && IslandPreviewHost.TryGetLineBounds(index, out var lineBounds))
            {
                return lineBounds;
            }
        }

        return null;
    }

    private void AnimateFocus(double scale, double tx, double ty)
    {
        // 布局反复触发时目标往往不变，重启动画会让它一直从头开始（看起来卡顿）。
        if (_hasFocusTarget && Math.Abs(scale - _focusTargetScale) < 0.001 &&
            Math.Abs(tx - _focusTargetX) < 0.5 && Math.Abs(ty - _focusTargetY) < 0.5)
        {
            return;
        }

        _hasFocusTarget = true;
        _focusTargetScale = scale;
        _focusTargetX = tx;
        _focusTargetY = ty;

        // 画布内是矢量/文字内容（课程表分段最多），变换动画时会被每帧重新光栅化；
        // 动画期间缓存成位图，缩放/平移只作用于位图，动画结束后撤掉缓存恢复清晰。
        PreviewCanvas.CacheMode ??= _focusCache ??= new BitmapCache();

        var token = ++_focusAnimationToken;
        var duration = TimeSpan.FromMilliseconds(260);
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
        var scaleX = new DoubleAnimation(scale, duration) { EasingFunction = ease };
        scaleX.Completed += (_, _) =>
        {
            if (token == _focusAnimationToken)
            {
                PreviewCanvas.CacheMode = null;
            }
        };
        FocusScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX);
        FocusScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(scale, duration) { EasingFunction = ease });
        FocusTranslate.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(tx, duration) { EasingFunction = ease });
        FocusTranslate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(ty, duration) { EasingFunction = ease });
    }
}
