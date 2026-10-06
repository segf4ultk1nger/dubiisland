using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ClassIsland.Core;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Controls;
using ClassIsland.Core.Controls.Ruleset;
using ClassIsland.Core.Enums.SettingsWindow;
using ClassIsland.Core.Models.Components;
using ClassIsland.Core.Services;
using ClassIsland.Services;
using ClassIsland.Shared.Helpers;
using ClassIsland.ViewModels.SettingsPages;
using CommunityToolkit.Mvvm.Input;
using GongSolutions.Wpf.DragDrop;
using Path = System.IO.Path;
using FormsScreen = System.Windows.Forms.Screen;
using CommonDialog = ClassIsland.Core.Controls.CommonDialog.CommonDialog;

namespace ClassIsland.Views.SettingPages;

/// <summary>
/// ComponentsSettingsPage.xaml 的交互逻辑
/// </summary>
[SettingsPageInfo("components", "组件", IconGlyphs.WidgetsOutline, IconGlyphs.Widgets, SettingsPageCategory.Internal)]
public partial class ComponentsSettingsPage : SettingsPageBase, IDropTarget
{
    public IComponentsService ComponentsService { get; }

    public ComponentsSettingsViewModel ViewModel { get; } = new();

    public SettingsService SettingsService { get; }

    public WallpaperPickingService WallpaperPickingService { get; }

    public ComponentsSettingsPage(
        IComponentsService componentsService,
        SettingsService settingsService,
        IThemeService themeService,
        ILessonsService lessonsService,
        IProfileService profileService,
        IExactTimeService exactTimeService,
        IRulesetService rulesetService,
        IWeatherService weatherService,
        WallpaperPickingService wallpaperPickingService)
    {
        SettingsService = settingsService;
        ComponentsService = componentsService;
        WallpaperPickingService = wallpaperPickingService;
        InitializeComponent();

        DataContext = this;
        SelectionHighlight.Data = _highlightGeometry;
        HoverHighlight.Data = _hoverGeometry;
        if (FindResource("DataProxy") is BindingProxy proxy)
        {
            proxy.Data = this;
        }

        IslandPreviewHost.Initialize(settingsService, themeService, componentsService, lessonsService, profileService,
            exactTimeService, rulesetService, weatherService);
    }

    private void OnSettingsOnPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        switch (args.PropertyName)
        {
            case nameof(SettingsService.Settings.CurrentComponentConfig):
                ViewModel.SelectedNode = null;
                break;
            case nameof(SettingsService.Settings.WindowDockingLocation):
            case nameof(SettingsService.Settings.WindowDockingMonitorIndex):
            case nameof(SettingsService.Settings.WindowDockingOffsetX):
            case nameof(SettingsService.Settings.WindowDockingOffsetY):
            case nameof(SettingsService.Settings.IsIgnoreWorkAreaEnabled):
            case nameof(SettingsService.Settings.Scale):
                UpdateIslandAlignment();
                UpdateWallpaperBackgroundIfNeeded();
                UpdatePreviewFocus();
                UpdatePreviewHighlight(false);
                break;
            case nameof(SettingsService.Settings.ComponentPreviewBackgroundMode):
                if (SettingsService.Settings.ComponentPreviewBackgroundMode == 3)
                {
                    _ = WallpaperPickingService.GetWallpaperAsync();
                }
                UpdatePreviewBackground();
                break;
            case nameof(SettingsService.Settings.IsComponentPreviewHighlightEnabled):
                UpdatePreviewHighlight();
                break;
        }
    }

    private void ComponentsSettingsPage_OnLoaded(object sender, RoutedEventArgs e)
    {
        SettingsService.Settings.PropertyChanged += OnSettingsOnPropertyChanged;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        WallpaperPickingService.PropertyChanged += OnWallpaperPickingServicePropertyChanged;
        ViewModel.IsCompact = RootGrid.ActualWidth > 0 && RootGrid.ActualWidth < CompactWidthThreshold;
        ViewModel.IsTreeVisible = true;
        _layoutReady = true;
        ApplyTreeLayout();
        UpdateIslandAlignment();
        UpdatePreviewBackground();
        UpdatePreviewHighlight();
        UpdatePreviewFocus();
        if (SettingsService.Settings.ComponentPreviewBackgroundMode == 3)
        {
            _ = WallpaperPickingService.GetWallpaperAsync();
        }
        SchedulePrewarmComponentSettings();
    }

    /// <summary>空闲时预热所有组件的设置控件，避免首次选中时套模板卡顿。</summary>
    private void SchedulePrewarmComponentSettings()
    {
        var all = ComponentsService.CurrentComponents.Lines
            .SelectMany(line => line.Children ?? Enumerable.Empty<ComponentSettings>());
        foreach (var component in EnumerateComponentSettings(all))
        {
            var target = component;
            Dispatcher.BeginInvoke(new System.Action(() => ComponentPresenter.Prewarm(target, PrewarmHost)),
                DispatcherPriority.ApplicationIdle);
        }
    }

    private static IEnumerable<ComponentSettings> EnumerateComponentSettings(IEnumerable<ComponentSettings> source)
    {
        foreach (var component in source)
        {
            yield return component;
            if (component.Children != null)
            {
                foreach (var child in EnumerateComponentSettings(component.Children))
                {
                    yield return child;
                }
            }
        }
    }

    private void ComponentsSettingsPage_OnUnloaded(object sender, RoutedEventArgs e)
    {
        SettingsService.Settings.PropertyChanged -= OnSettingsOnPropertyChanged;
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        WallpaperPickingService.PropertyChanged -= OnWallpaperPickingServicePropertyChanged;
    }

    private void TreeComponents_OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        ViewModel.SelectedNode = e.NewValue;
        HideTreeIfCompact();
    }

    /// <summary>行背景铺满整宽，缩进由层级换算的 Padding 模拟。</summary>
    private void ComponentTreeItem_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TreeViewItem item)
            return;
        var depth = 0;
        for (var parent = ItemsControl.ItemsControlFromItemContainer(item);
             parent is TreeViewItem parentItem;
             parent = ItemsControl.ItemsControlFromItemContainer(parentItem))
        {
            depth++;
        }

        item.Padding = new Thickness(8 + depth * 18, 0, 8, 0);
    }

    /// <summary>把 ListBox 截获的滚轮事件转给外层 ScrollViewer，避免鼠标悬停在对齐按钮上时无法滚动页面。</summary>
    private void UIElement_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled)
            return;
        e.Handled = true;
        var scrollViewer = FindAncestorScrollViewer(sender as DependencyObject);
        scrollViewer?.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta / 3.0);
    }

    private static ScrollViewer? FindAncestorScrollViewer(DependencyObject? d)
    {
        while (d != null)
        {
            if (d is ScrollViewer scrollViewer)
                return scrollViewer;
            d = VisualTreeHelper.GetParent(d);
        }

        return null;
    }

    private void ContainerComponentsSource_OnFilter(object sender, FilterEventArgs e)
    {
        if (e.Item is not ComponentInfo info)
        {
            return;
        }

        e.Accepted = info.IsComponentContainer;
    }

    private void NonContainerComponentsSource_OnFilter(object sender, FilterEventArgs e)
    {
        if (e.Item is not ComponentInfo info)
        {
            return;
        }

        e.Accepted = !info.IsComponentContainer;
    }

    #region 预览（背景 / 高亮）

    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ComponentsSettingsViewModel.SelectedNode))
        {
            UpdatePreviewHighlight();
            UpdatePreviewFocus();
        }
    }

    private void OnWallpaperPickingServicePropertyChanged(object? sender, PropertyChangedEventArgs e)
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

    private void ButtonMore_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || FindResource("MoreMenu") is not ContextMenu menu)
            return;
        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void UpdatePreviewBackground()
    {
        switch (SettingsService.Settings.ComponentPreviewBackgroundMode)
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
        if (SettingsService.Settings.ComponentPreviewBackgroundMode == 3)
        {
            UpdatePreviewBackground();
        }
    }

    /// <summary>预览里的岛按停靠位置贴左上/中/右下，而不是永远居中。</summary>
    private void UpdateIslandAlignment()
    {
        var location = SettingsService.Settings.WindowDockingLocation;
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
        var image = WallpaperPickingService.WallpaperImage;
        var content = IslandPreviewHost.ContentSize;
        if (image == null || image.PixelWidth <= 0 || content.Width <= 0 ||
            PreviewBorder.ActualWidth <= 0 || PreviewBorder.ActualHeight <= 0)
        {
            return null;
        }

        var settings = SettingsService.Settings;
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
        if (!SettingsService.Settings.IsComponentPreviewHighlightEnabled ||
            TryGetNodePreviewBounds(ViewModel.SelectedNode) is not { } bounds)
        {
            // 消失不做动画。
            _highlightGeometry.BeginAnimation(RectangleGeometry.RectProperty, null);
            SelectionHighlight.Visibility = Visibility.Collapsed;
            _highlightVisible = false;
            return;
        }

        // 出现不做动画；选中项之间过渡做动画（仅非紧凑模式）。
        if (_highlightVisible && animate && !ViewModel.IsCompact)
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

    private void TreeComponents_OnMouseMove(object sender, MouseEventArgs e)
    {
        // 命中整行（含行内留白），而不是只看文字那一小块。
        var item = ComponentTreeHitTest.GetItem(TreeComponents, e.GetPosition(TreeComponents));
        SetHoveredNode(item?.DataContext);
    }

    private void TreeComponents_OnMouseLeave(object sender, MouseEventArgs e) => SetHoveredNode(null);

    private void SetHoveredNode(object? node)
    {
        if (ReferenceEquals(node, _hoveredNode))
        {
            return;
        }

        _hoveredNode = node;
        UpdateHoverHighlight();
    }

    /// <summary>预览里实时指示鼠标所在的组件/行（蓝框，无动画）。</summary>
    private void UpdateHoverHighlight()
    {
        if (TryGetNodePreviewBounds(_hoveredNode) is not { } bounds)
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
        if (TryGetNodePreviewBounds(ViewModel.SelectedNode) is not { } bounds ||
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
        if (TryGetNodeBounds(node) is not { } r || r.Width <= 0 || r.Height <= 0)
        {
            return null;
        }

        var scale = SettingsService.Settings.Scale <= 0 ? 1.0 : SettingsService.Settings.Scale;
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

        if (node is MainWindowLineSettings line)
        {
            var index = ComponentsService.CurrentComponents.Lines.IndexOf(line);
            if (index >= 0 && IslandPreviewHost.TryGetLineBounds(index, out var lineBounds))
            {
                return lineBounds;
            }
        }

        return null;
    }

    private readonly RectangleGeometry _highlightGeometry = new();
    private readonly RectangleGeometry _hoverGeometry = new();
    private bool _highlightVisible;
    private object? _hoveredNode;
    private BitmapCache? _focusCache;
    private int _focusAnimationToken;
    private bool _hasFocusTarget;
    private double _focusTargetScale = 1;
    private double _focusTargetX;
    private double _focusTargetY;

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

    #endregion

    #region 紧凑模式

    private const double CompactWidthThreshold = 660;

    private bool _layoutReady;

    /// <summary>页面过窄时进入紧凑模式：组件树与属性面板互斥全宽。</summary>
    private void RootGrid_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width <= 0)
            return;
        var compact = e.NewSize.Width < CompactWidthThreshold;
        if (_layoutReady && ViewModel.IsCompact == compact)
            return;
        ViewModel.IsCompact = compact;
        ViewModel.IsTreeVisible = true;
        _layoutReady = true;
        ApplyTreeLayout();
    }

    private void ButtonToggleTree_OnClick(object sender, RoutedEventArgs e)
    {
        // 紧凑模式下树与属性面板互斥：从属性面板返回树时清空选中，避免树里残留高亮。
        if (ViewModel.IsCompact && !ViewModel.IsTreeVisible && ViewModel.SelectedNode is { } selected)
        {
            if (FindContainer(TreeComponents, selected) is { } container)
            {
                container.IsSelected = false;
            }
            ViewModel.SelectedNode = null;
        }
        ViewModel.IsTreeVisible = !ViewModel.IsTreeVisible;
        ApplyTreeLayout();
    }

    private void ApplyTreeLayout()
    {
        var showTree = ViewModel.IsTreeVisible;
        var treeFullWidth = showTree && ViewModel.IsCompact;

        if (!showTree)
        {
            TreeColumn.MinWidth = 0;
            TreeColumn.Width = new GridLength(0);
            SplitterColumn.Width = new GridLength(0);
            Splitter.Visibility = Visibility.Collapsed;
        }
        else if (treeFullWidth)
        {
            // 紧凑模式：树与详情互斥全宽。
            TreeColumn.MinWidth = 0;
            TreeColumn.Width = new GridLength(1, GridUnitType.Star);
            SplitterColumn.Width = new GridLength(0);
            Splitter.Visibility = Visibility.Collapsed;
        }
        else
        {
            TreeColumn.MinWidth = 180;
            TreeColumn.Width = new GridLength(230);
            SplitterColumn.Width = GridLength.Auto;
            Splitter.Visibility = Visibility.Visible;
        }

        DetailColumn.MinWidth = treeFullWidth ? 0 : 320;
        DetailColumn.Width = treeFullWidth ? new GridLength(0) : new GridLength(1, GridUnitType.Star);

        TreePanel.Visibility = showTree ? Visibility.Visible : Visibility.Collapsed;
    }

    private void HideTreeIfCompact()
    {
        if (ViewModel.IsCompact && ViewModel.IsTreeVisible)
        {
            ViewModel.IsTreeVisible = false;
            ApplyTreeLayout();
        }
    }

    #endregion

    #region 配置方案

    private void ButtonRefresh_OnClick(object sender, RoutedEventArgs e)
    {
        ComponentsService.RefreshConfigs();
    }

    private async void ButtonCreateConfig_OnClick(object sender, RoutedEventArgs e)
    {
        ViewModel.CreateProfileName = "";
        if (FindResource("CreateProfileDialog") is not FrameworkElement content)
            return;
        content.DataContext = this;
        var r = await DialogService.ShowAsync(content, DialogHostIdentifier);
        Debug.WriteLine(r);

        var path = Path.Combine(ClassIsland.Services.ComponentsService.ComponentSettingsPath,
            ViewModel.CreateProfileName + ".json");
        if (r == null || File.Exists(path))
        {
            return;
        }
        ConfigureFileHelper.SaveConfig(path, ClassIsland.Services.ComponentsService.DefaultComponentProfile);
        ComponentsService.RefreshConfigs();
        SettingsService.Settings.CurrentComponentConfig = ViewModel.CreateProfileName;
        ViewModel.SelectedNode = null;
    }

    private void ButtonOpenConfigFolder_OnClick(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo()
        {
            FileName = Path.GetFullPath(ClassIsland.Services.ComponentsService.ComponentSettingsPath),
            UseShellExecute = true
        });
    }

    #endregion

    #region 添加组件

    private void ButtonAddComponent_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || FindResource("AddComponentMenu") is not ContextMenu menu)
            return;
        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    [RelayCommand]
    private void AddComponent(ComponentInfo info)
    {
        var list = EnsureInsertList();
        var settings = new ComponentSettings { Id = info.Guid.ToString() };
        if (info.ComponentType?.BaseType != null)
        {
            ClassIsland.Services.ComponentsService.LoadComponentSettings(settings, info.ComponentType.BaseType);
        }
        list.Add(settings);
        SetSelectedNode(settings);
    }

    private ObservableCollection<ComponentSettings> EnsureInsertList()
    {
        if (ViewModel.SelectedComponent?.Children is { } containerChildren)
            return containerChildren;
        if (ViewModel.SelectedLine?.Children is { } lineChildren)
            return lineChildren;
        var last = ComponentsService.CurrentComponents.Lines.LastOrDefault();
        if (last != null)
            return last.Children;
        var newLine = new MainWindowLineSettings();
        ComponentsService.CurrentComponents.Lines.Add(newLine);
        return newLine.Children;
    }

    #endregion

    #region 组件操作

    [RelayCommand]
    private void RemoveComponent(ComponentSettings settings)
    {
        FindContainingList(settings)?.Remove(settings);
        if (ReferenceEquals(ViewModel.SelectedNode, settings))
        {
            ViewModel.SelectedNode = null;
        }
    }

    [RelayCommand]
    private void DuplicateComponent(ComponentSettings settings)
    {
        var list = FindContainingList(settings);
        if (list == null)
            return;
        var copy = ConfigureFileHelper.CopyObject(settings);
        list.Insert(Math.Max(0, list.IndexOf(settings)), copy);
        SetSelectedNode(copy);
    }

    [RelayCommand]
    private void MoveComponentUp(ComponentSettings settings)
    {
        var list = FindContainingList(settings);
        if (list == null)
            return;
        var index = list.IndexOf(settings);
        if (index > 0)
        {
            list.Move(index, index - 1);
        }
    }

    [RelayCommand]
    private void MoveComponentDown(ComponentSettings settings)
    {
        var list = FindContainingList(settings);
        if (list == null)
            return;
        var index = list.IndexOf(settings);
        if (index >= 0 && index < list.Count - 1)
        {
            list.Move(index, index + 1);
        }
    }

    [RelayCommand]
    private void MoveOutOfContainer(ComponentSettings settings)
    {
        var list = FindContainingList(settings);
        var line = FindContainingLine(settings);
        if (list == null || line == null || ReferenceEquals(list, line.Children))
        {
            return;
        }
        list.Remove(settings);
        line.Children.Add(settings);
    }

    [RelayCommand]
    private void CreateContainerComponent(ComponentInfo container)
    {
        if (ViewModel.ContextMenuTarget is not ComponentSettings target)
            return;
        var list = FindContainingList(target);
        if (list == null)
            return;

        var index = Math.Max(0, list.IndexOf(target));
        var containerSettings = new ComponentSettings { Id = container.Guid.ToString() };
        if (container.ComponentType?.BaseType != null)
        {
            containerSettings.Settings =
                ClassIsland.Services.ComponentsService.LoadComponentSettings(containerSettings, container.ComponentType.BaseType);
        }
        list.Insert(index, containerSettings);
        list.Remove(target);
        containerSettings.Children?.Add(target);
        SetSelectedNode(containerSettings);
    }

    #endregion

    #region 行操作

    private void ButtonCreateMainWindowLine_OnClick(object sender, RoutedEventArgs e) => InsertLineAfter(ViewModel.SelectedLine);

    private void MenuItemInsertLine_OnClick(object sender, RoutedEventArgs e) =>
        InsertLineAfter(ViewModel.ContextMenuTarget as MainWindowLineSettings ?? ViewModel.SelectedLine);

    private void InsertLineAfter(MainWindowLineSettings? anchor)
    {
        var lines = ComponentsService.CurrentComponents.Lines;
        var index = anchor == null ? lines.Count : Math.Min(lines.Count, lines.IndexOf(anchor) + 1);
        var line = new MainWindowLineSettings();
        lines.Insert(index, line);
        SetSelectedNode(line);
    }

    private void MenuItemSetMainLine_OnClick(object sender, RoutedEventArgs e)
    {
        if ((ViewModel.ContextMenuTarget as MainWindowLineSettings ?? ViewModel.SelectedLine) is not { } line)
            return;
        SetMainLine(line);
    }

    private void ButtonSetMainLine_OnClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is MainWindowLineSettings line)
        {
            SetMainLine(line);
        }
    }

    private void SetMainLine(MainWindowLineSettings line)
    {
        foreach (var other in ComponentsService.CurrentComponents.Lines)
        {
            other.IsMainLine = ReferenceEquals(other, line);
        }
    }

    private void MenuItemRemoveLine_OnClick(object sender, RoutedEventArgs e)
    {
        if ((ViewModel.ContextMenuTarget as MainWindowLineSettings ?? ViewModel.SelectedLine) is not { } line)
            return;
        var lines = ComponentsService.CurrentComponents.Lines;
        if (lines.Count <= 1)
        {
            CommonDialog.ShowError("至少需要保留 1 个主界面行。");
            return;
        }
        lines.Remove(line);
        if (ReferenceEquals(ViewModel.SelectedNode, line))
        {
            ViewModel.SelectedNode = null;
        }
    }

    private void ButtonOpenRuleset_OnClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ComponentSettings component ||
            FindResource("RulesetControl") is not RulesetControl control)
        {
            return;
        }
        control.Ruleset = component.HidingRules;
        OpenDrawer("RulesetControl");
    }

    #endregion

    #region 右键菜单

    private void ComponentOperationContextMenu_OnOpened(object sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu { PlacementTarget: FrameworkElement { DataContext: ComponentSettings settings } })
        {
            ViewModel.ContextMenuTarget = settings;
        }
    }

    private void LineOperationContextMenu_OnOpened(object sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu { PlacementTarget: FrameworkElement { DataContext: MainWindowLineSettings line } })
        {
            ViewModel.ContextMenuTarget = line;
        }
    }

    #endregion

    #region 辅助

    private ObservableCollection<ComponentSettings>? FindContainingList(ComponentSettings target)
    {
        foreach (var line in ComponentsService.CurrentComponents.Lines)
        {
            var found = FindIn(line.Children, target);
            if (found != null)
                return found;
        }
        return null;
    }

    private static ObservableCollection<ComponentSettings>? FindIn(ObservableCollection<ComponentSettings> list, ComponentSettings target)
    {
        if (list.Contains(target))
            return list;
        foreach (var component in list)
        {
            if (component.Children is { } children)
            {
                var found = FindIn(children, target);
                if (found != null)
                    return found;
            }
        }
        return null;
    }

    private MainWindowLineSettings? FindContainingLine(ComponentSettings target)
    {
        foreach (var line in ComponentsService.CurrentComponents.Lines)
        {
            if (ContainsRecursive(line.Children, target))
                return line;
        }
        return null;
    }

    private static bool ContainsRecursive(ObservableCollection<ComponentSettings> list, ComponentSettings target)
    {
        foreach (var component in list)
        {
            if (ReferenceEquals(component, target))
                return true;
            if (component.Children is { } children && ContainsRecursive(children, target))
                return true;
        }
        return false;
    }

    /// <summary>设置选中节点，并同步树的选中态（否则点击已选中的项不会再触发事件，右侧面板不更新）。</summary>
    private void SetSelectedNode(object? node)
    {
        ViewModel.SelectedNode = node;
        if (node == null)
            return;
        TreeComponents.UpdateLayout();
        if (FindContainer(TreeComponents, node) is { } container)
        {
            container.IsSelected = true;
            container.BringIntoView();
        }
        HideTreeIfCompact();
    }

    private static TreeViewItem? FindContainer(ItemsControl parent, object item)
    {
        if (parent.ItemContainerGenerator.ContainerFromItem(item) is TreeViewItem direct)
            return direct;
        foreach (var child in parent.Items)
        {
            if (parent.ItemContainerGenerator.ContainerFromItem(child) is TreeViewItem childContainer &&
                FindContainer(childContainer, item) is { } found)
            {
                return found;
            }
        }
        return null;
    }

    #endregion

    #region 拖拽

    public void DragEnter(IDropInfo dropInfo)
    {
    }

    public void DragLeave(IDropInfo dropInfo)
    {
    }

    public void DragOver(IDropInfo dropInfo)
    {
        if (dropInfo.Data is not ComponentSettings settings) { dropInfo.Effects = DragDropEffects.None; return; }
        var container = GetHoveredItem(dropInfo);
        if (container == null) { dropInfo.Effects = DragDropEffects.None; return; }
        var (list, _) = ComputeInsert(container, GetFraction(container, dropInfo));
        if (list == null || CollectionBelongsToDescendant(list, settings))
        {
            dropInfo.Effects = DragDropEffects.None;
            return;
        }
        dropInfo.DropTargetAdorner = typeof(TreeInsertAdorner);
        dropInfo.Effects = DragDropEffects.Move;
    }

    public void Drop(IDropInfo dropInfo)
    {
        if (dropInfo.Data is not ComponentSettings settings) return;
        var container = GetHoveredItem(dropInfo);
        if (container == null) return;
        var (targetList, insertIndex) = ComputeInsert(container, GetFraction(container, dropInfo));
        var sourceList = FindContainingList(settings);
        if (targetList == null || sourceList == null || CollectionBelongsToDescendant(targetList, settings))
        {
            return;
        }

        if (ReferenceEquals(sourceList, targetList))
        {
            var oldIndex = sourceList.IndexOf(settings);
            if (oldIndex < 0)
                return;
            if (insertIndex > oldIndex)
                insertIndex--;
            insertIndex = Math.Max(0, Math.Min(insertIndex, sourceList.Count - 1));
            if (insertIndex != oldIndex)
            {
                sourceList.Move(oldIndex, insertIndex);
            }
        }
        else
        {
            insertIndex = Math.Max(0, Math.Min(insertIndex, targetList.Count));
            sourceList.Remove(settings);
            targetList.Insert(insertIndex, settings);
        }
        SetSelectedNode(settings);
    }

    /// <summary>
    /// 按光标在悬停项内的纵向位置计算插入目标：上 1/3 插到该项之前，下 1/3 插到之后，
    /// 中间 1/3 落到容器/行内部（追加为子项）；非容器项中间 1/3 视为插到之后。
    /// </summary>
    private (ObservableCollection<ComponentSettings>? List, int Index) ComputeInsert(TreeViewItem container, double fy)
    {
        switch (container.DataContext)
        {
            case MainWindowLineSettings line:
                return (line.Children, fy < 0.5 ? 0 : line.Children.Count);
            case ComponentSettings component:
                if (fy >= 0.33 && fy <= 0.67 && component.Children is { } children)
                {
                    return (children, children.Count);
                }
                var parent = FindContainingList(component);
                if (parent == null)
                {
                    return (null, 0);
                }
                var index = parent.IndexOf(component);
                return (parent, fy < 0.5 ? index : index + 1);
            default:
                return (null, 0);
        }
    }

    private TreeViewItem? GetHoveredItem(IDropInfo dropInfo)
        => ComponentTreeHitTest.GetItem(dropInfo.VisualTarget, dropInfo.DropPosition)
           ?? dropInfo.VisualTargetItem as TreeViewItem;

    private static double GetFraction(TreeViewItem container, IDropInfo dropInfo)
    {
        var height = container.ActualHeight;
        if (height <= 0 || dropInfo.VisualTarget is not { } target)
            return 0.5;
        try
        {
            var top = container.TransformToAncestor(target).Transform(new Point(0, 0)).Y;
            return (dropInfo.DropPosition.Y - top) / height;
        }
        catch
        {
            return 0.5;
        }
    }

    private static bool CollectionBelongsToDescendant(ObservableCollection<ComponentSettings> list, ComponentSettings component)
    {
        if (component.Children is not { } children)
            return false;
        if (ReferenceEquals(children, list))
            return true;
        foreach (var child in children)
        {
            if (CollectionBelongsToDescendant(list, child))
                return true;
        }
        return false;
    }

    #endregion
}
