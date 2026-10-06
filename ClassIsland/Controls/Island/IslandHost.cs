using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ClassIsland.Controls.Island.Components;
using ClassIsland.Controls.NotificationEffects;
using ClassIsland.Controls.NotificationProviders;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Helpers.Native;
using ClassIsland.Core.Models.Notification;
using ClassIsland.Core.Models.Notification.Templates;
using ClassIsland.Core.Models.Theming;
using ClassIsland.Models;
using ClassIsland.Models.ComponentSettings;
using ClassIsland.Models.EventArgs;
using ClassIsland.Services;
using ClassIsland.Shared.Enums;
using ClassIsland.ViewModels;
using ClassIsland.Views;
using Linearstar.Windows.RawInput;
using MahApps.Metro.IconPacks;
using Windows.Win32.UI.Accessibility;
using Windows.Win32.UI.Controls;

namespace ClassIsland.Controls.Island;

/// <summary>
/// 岛的纯自绘宿主。基于裸 <see cref="HwndSource"/>，无 XAML、无 <see cref="Window"/> 壳，
/// 内容为单个 <see cref="DrawingVisual"/>。
/// </summary>
public sealed class IslandHost : IDisposable, INotificationVisualHost
{
    private const int WS_POPUP = unchecked((int)0x80000000);
    private const int WS_EX_NOACTIVATE = 0x08000000;

    private readonly SettingsService _settingsService;
    private readonly IThemeService _themeService;
    private readonly ILessonsService _lessonsService;
    private readonly IComponentsService _componentsService;
    private readonly IWeatherService _weatherService;
    private readonly IWindowRuleService _windowRuleService;
    private readonly IRulesetService _rulesetService;
    private readonly ClassIsland.Services.Scripting.ScriptConditionEvaluator _conditionEvaluator;
    private readonly MainViewModel _viewModel;
    private readonly NotificationDisplayService _notificationDisplayService;
    private readonly TopmostEffectWindow _topmostEffectWindow;
    private readonly IslandContext _context;
    private readonly IslandRenderer _renderer;
    private readonly IslandSurface _surface;
    private readonly DispatcherTimer _topmostRecheckTimer = new();

    private HwndSource? _source;
    private IntPtr _hwnd;
    private double _dpiX = 1.0;
    private double _dpiY = 1.0;
    private int _windowLeftPx;
    private int _windowTopPx;
    private int _contentWidthPx;
    private int _contentHeightPx;
    private bool _isVisible;
    private bool _forceLayered;

    private AnimSpec? _animSpec;
    private TimeSpan? _animStart;
    private bool _maskClosing;
    private bool _renderingHooked;
    private bool _showAnimActive;
    private TimeSpan? _showAnimStart;
    private double _showFromScale = 1;
    private double _showFromOpacity = 1;
    private bool _hideAnimActive;
    private TimeSpan? _hideAnimStart;
    private double _hideFromScale = 1;
    private double _hideFromOpacity = 1;
    private static readonly Dictionary<int, Dictionary<string, AnimSpec>> AnimSpecsByStyle = BuildAllAnimSpecs();

    private const double ShowScaleMin = 0.89;
    private const double ShowAnimatedDuration = 0.47;
    private const double ShowFadePortion = 0.78;
    private const double HideAnimatedDuration = 0.13;
    private const double ElasticConst = 2 * Math.PI / 0.3;
    private const double ElasticConst2 = 0.3 / 4;
    private static readonly double ExpoOffset = Math.Pow(2, -10);
    private static readonly double ElasticOffsetHalf =
        Math.Pow(2, -10) * Math.Sin((0.5 - ElasticConst2) * ElasticConst);

    /// <summary>出现动画期间 scale 的峰值（用于窗口过冲预留，避免弹性回弹峰值被窗口裁切）。</summary>
    private static readonly double ShowMaxScale = ComputeShowMaxScale();

    private static double ComputeShowMaxScale()
    {
        var max = 1.0;
        for (var i = 0; i <= 1000; i++)
        {
            var p = i / 1000.0;
            var s = ShowScaleMin + (1 - ShowScaleMin) * OutElasticHalf(p);
            if (s > max)
                max = s;
        }

        return max + 0.005;
    }

    public Settings Settings => _settingsService.Settings;

    public IslandContext Context => _context;

    public IslandRenderer Renderer => _renderer;

    public IntPtr Handle => _hwnd;

    public IslandHost(
        SettingsService settingsService,
        IThemeService themeService,
        ILessonsService lessonsService,
        IProfileService profileService,
        IExactTimeService exactTimeService,
        IComponentsService componentsService,
        IWeatherService weatherService,
        IWindowRuleService windowRuleService,
        IRulesetService rulesetService,
        ClassIsland.Services.Scripting.ScriptConditionEvaluator conditionEvaluator,
        MainViewModel viewModel,
        NotificationDisplayService notificationDisplayService,
        TopmostEffectWindow topmostEffectWindow)
    {
        _settingsService = settingsService;
        _themeService = themeService;
        _lessonsService = lessonsService;
        _componentsService = componentsService;
        _weatherService = weatherService;
        _windowRuleService = windowRuleService;
        _rulesetService = rulesetService;
        _conditionEvaluator = conditionEvaluator;
        _viewModel = viewModel;
        _notificationDisplayService = notificationDisplayService;
        _topmostEffectWindow = topmostEffectWindow;

        _context = new IslandContext(settingsService.Settings, lessonsService, profileService, exactTimeService,
            rulesetService, conditionEvaluator, weatherService)
        {
            AccentColor = themeService.PrimaryColor
        };
        _renderer = new IslandRenderer(_context);
        _surface = new IslandSurface(_renderer);

        HookComponentCollections();
        BuildComponents();
        _componentsService.PropertyChanged += OnComponentsServicePropertyChanged;

        _topmostRecheckTimer.Tick += OnTopmostRecheckTick;
        _windowRuleService.ForegroundWindowChanged += OnForegroundWindowChanged;
        _rulesetService.StatusUpdated += OnRulesetStatusUpdated;
        _viewModel.PropertyChanged += OnNotificationChanged;
        _notificationDisplayService.AnimationEvent += OnMainWindowAnimation;
        lessonsService.PostMainTimerTicked += OnLessonsTicked;
        lessonsService.CurrentTimeStateChanged += OnLessonsTicked;
        settingsService.Settings.PropertyChanged += OnSettingsChanged;
        themeService.ThemeUpdated += OnThemeUpdated;
    }

    /// <summary>创建底层窗口并显示。</summary>
    public void Show()
    {
        if (_source != null)
            return;
        // 自绘模式下没有 MainWindow 来登记视觉壳层，由岛自己接收提醒特效回调。
        if (Settings.UseSelfDrawnIsland)
            _notificationDisplayService.VisualHost = this;
        CreateSource();
        RefreshTheme();
        UpdateWindowPos();
        ApplyWindowStyles();
        UpdateVisibility();
        // 窗口显示后再启用强制 layered（点击穿透需要），并重新应用样式触发补回。
        _forceLayered = true;
        if (_isVisible)
            ApplyWindowStyles();
    }

    /// <summary>销毁底层窗口。</summary>
    public void Close()
    {
        if (_source == null)
            return;
        _source.RemoveHook(WndProc);
        _source.RootVisual = null;
        _source.Dispose();
        _source = null;
        _hwnd = IntPtr.Zero;
    }

    public void Dispose()
    {
        UnhookComponentCollections();
        _componentsService.PropertyChanged -= OnComponentsServicePropertyChanged;
        _settingsService.Settings.PropertyChanged -= OnSettingsChanged;
        _themeService.ThemeUpdated -= OnThemeUpdated;
        _lessonsService.PostMainTimerTicked -= OnLessonsTicked;
        _lessonsService.CurrentTimeStateChanged -= OnLessonsTicked;
        _windowRuleService.ForegroundWindowChanged -= OnForegroundWindowChanged;
        _rulesetService.StatusUpdated -= OnRulesetStatusUpdated;
        _viewModel.PropertyChanged -= OnNotificationChanged;
        _notificationDisplayService.AnimationEvent -= OnMainWindowAnimation;
        StopRenderingHook();
        _topmostRecheckTimer.Stop();
        _renderer.Invalidated -= OnRendererInvalidated;
        Close();
    }

    private void CreateSource()
    {
        var compatible = Settings.IsCompatibleWindowTransparentEnabled;
        var exStyle = NativeWindowHelper.WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;

        var parameters = new HwndSourceParameters("LegacyIsland")
        {
            WindowStyle = WS_POPUP,
            ExtendedWindowStyle = exStyle,
            PositionX = 0,
            PositionY = 0,
            Width = 1,
            Height = 1,
            UsesPerPixelOpacity = compatible
        };
        var source = new HwndSource(parameters);
        source.RootVisual = _surface;
        _surface.RenderTransformOrigin = new Point(0.5, 0.5);
        _surface.RenderTransform = new ScaleTransform(1, 1);
        // 窗口始终按弹性过冲峰值预留（内容居中，四周留一条透明边）。这样出现动画全程窗口尺寸完全不变，
        // 不会因改窗口尺寸而闪烁；峰值 scale 也不被裁。
        _surface.WindowOvershootScale = ShowMaxScale;
        source.AddHook(WndProc);
        _source = source;
        _hwnd = source.Handle;

        if (!compatible)
        {
            // GPU 路径（照搬 WindowChromeWorker._ExtendGlassFrame）：非分层窗口 + 透明背景色 + DWM 玻璃框，
            // WPF 自绘内容按逐像素 alpha 由 DWM 合成。关键：必须设 CompositionTarget.BackgroundColor=Transparent。
            source.CompositionTarget.BackgroundColor = Colors.Transparent;
            var margins = new MARGINS { cxLeftWidth = -1, cxRightWidth = -1, cyTopHeight = -1, cyBottomHeight = -1 };
            DwmExtendFrameIntoClientArea((HWND)_hwnd, in margins);
        }

        _renderer.Invalidated += OnRendererInvalidated;
        UpdateDpi();

        if (Settings.UseRawInput)
        {
            try
            {
                RawInputDevice.RegisterDevice(HidUsageAndPage.Mouse, RawInputDeviceFlags.InputSink, _hwnd);
            }
            catch
            {
                // 注册失败时回退到定时轮询（OnLessonsTicked 里的 UpdateMouseIn）。
            }
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // 点击穿透需要 WS_EX_LAYERED + WS_EX_TRANSPARENT，但 HwndTarget 在 UsesPerPixelOpacity=false 时会
        // 移除 WS_EX_LAYERED。这里在自己的 Hook（晚于 HwndTarget）里补回，且只在窗口显示后再启用，
        // 避免创建期补 layered 导致内容不合成。
        if (_forceLayered && msg == 0x007C && (long)wParam == (long)WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE)
        {
            var styleStruct = (NativeWindowHelper.StyleStruct)Marshal.PtrToStructure(lParam,
                typeof(NativeWindowHelper.StyleStruct));
            styleStruct.styleNew |= NativeWindowHelper.WS_EX_LAYERED;
            Marshal.StructureToPtr(styleStruct, lParam, false);
            handled = true;
        }

        if (msg == 0x00FF) // WM_INPUT
        {
            try
            {
                if (RawInputData.FromHandle(lParam) is RawInputMouseData)
                    UpdateMouseIn();
            }
            catch
            {
                // 忽略无效的 RawInput 数据
            }
        }

        if (msg == 0x0047 && Settings.WindowTopmostRecheckMode == 0) // WM_WINDOWPOSCHANGED
        {
            var pos = Marshal.PtrToStructure<WINDOWPOS>(lParam);
            if ((pos.flags & SET_WINDOW_POS_FLAGS.SWP_NOZORDER) == 0)
            {
                if (pos.hwndInsertAfter != NativeWindowHelper.HWND_TOPMOST)
                    ReCheckTopmostState();
                if (pos.hwndInsertAfter != NativeWindowHelper.HWND_BOTTOM)
                    SetBottom();
            }
        }

        return IntPtr.Zero;
    }

    private void OnLessonsTicked(object? sender, EventArgs e)
    {
        UpdateVisibility();
        UpdateMouseIn();
        UpdateNotification();
        _renderer.Invalidate();
    }

    private void OnRulesetStatusUpdated(object? sender, EventArgs e)
    {
        UpdateVisibility();
        _renderer.Invalidate();
    }

    /// <summary>按组件服务的当前配置重建整个组件树。</summary>
    private void BuildComponents()
    {
        _renderer.Clear();
        var lines = _componentsService.CurrentComponents.Lines;
        for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            foreach (var settings in lines[lineIndex].Children)
            {
                var component = IslandComponentFactory.Create(settings, _context);
                if (component == null)
                    continue;
                component.LineNumber = lineIndex;
                _renderer.Add(component);
            }
        }
    }

    /// <summary>订阅顶层行与各行的组件集合变化，用于重建组件树。</summary>
    private void HookComponentCollections()
    {
        var lines = _componentsService.CurrentComponents.Lines;
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
        var lines = _componentsService.CurrentComponents.Lines;
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
    /// 若每次都同步重建（含窗口重排），中间态会在渲染线程上画出来（界面看起来“分两步”）。
    /// </summary>
    private void RequestBuildComponents()
    {
        if (_rebuildScheduled)
            return;
        _rebuildScheduled = true;
        _surface.Dispatcher.BeginInvoke(new Action(() =>
        {
            _rebuildScheduled = false;
            BuildComponents();
        }), DispatcherPriority.Render);
    }

    private void OnNotificationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is "CurrentMaskContent" or "CurrentOverlayContent")
            UpdateNotification();
    }

    /// <summary>从 MainWindow 的提醒状态提取自绘所需的遮罩 / overlay 文本。</summary>
    private void UpdateNotification()
    {
        var viewModel = _viewModel;
        _renderer.UseSlantedMask = Settings.IslandMaskAnimationStyle == 2;
        var mask = viewModel.CurrentMaskContent;
        if (mask != null && (!_renderer.IsMaskVisible || _maskClosing))
        {
            // 新遮罩出现：复位到动画起点，等 OverlayMaskIn 驱动。
            _maskClosing = false;
            _renderer.MaskContentOpacity = 0;
            _renderer.ContentOpacity = 1;
            if (_renderer.UseSlantedMask)
            {
                _renderer.MaskOffsetY = 0;
                _renderer.MaskContentScale = 1.1;
                _renderer.ResetMaskRegions(0);
            }
            else
            {
                _renderer.MaskOffsetY = -60;
                _renderer.MaskContentScale = 1;
            }
        }

        if (mask == null)
        {
            // 遮罩内容已清空：不要立即隐藏，等收合动画播完再收起（见 OnRendering）。
            if (_renderer.IsMaskVisible)
                _maskClosing = true;
        }
        else
        {
            _renderer.IsMaskVisible = true;
        }

        // 收合动画期间保留遮罩文本（内容已清空），由 MaskContentOpacity 淡出，播完再清。
        if (mask != null || !_maskClosing)
            UpdateMaskVisual(mask);
        var overlay = viewModel.CurrentOverlayContent;
        _renderer.IsOverlayVisible = overlay != null;
        _renderer.OverlayText = ExtractOverlayText(overlay);

        if (mask == null && overlay == null && !_maskClosing)
        {
            FinalizeNotificationVisual();
        }
        else if (_maskClosing && _animSpec is null)
        {
            // 没有正在跑的收合动画（例如已被覆盖或本就没有），直接收起。
            FinalizeNotificationVisual();
        }

        _renderer.Invalidate();
    }

    /// <summary>提醒结束：复位遮罩 / overlay 视觉态并停止逐帧动画。</summary>
    private void FinalizeNotificationVisual()
    {
        _renderer.MaskOffsetY = 0;
        _renderer.MaskContentOpacity = 1;
        _renderer.MaskContentScale = 1;
        _renderer.ResetMaskRegions(0);
        _renderer.OverlayOpacity = 1;
        _renderer.ContentOpacity = 1;
        _renderer.IsMaskVisible = false;
        _maskClosing = false;
        _animSpec = null;
        _animStart = null;
    }

    private void OnMainWindowAnimation(object? sender, MainWindowAnimationEventArgs e)
        => StartNotificationAnimation(e.StoryboardName);

    #region INotificationVisualHost

    public void OnNotificationTopmostChanged()
    {
        // 与 MainWindow 保持一致：置底模式下显示提醒时需要临时置顶，提醒结束后再回到置底。
        if (_viewModel.IsNotificationWindowExplicitShowed && Settings.WindowLayer == 0)
        {
            UpdateLayer();
            ReCheckTopmostState();
        }
        else if (!_viewModel.IsNotificationWindowExplicitShowed)
        {
            SetBottom();
            UpdateLayer();
        }
    }

    public void OnNotificationEffectRequested()
    {
        if (!_isVisible || Settings.IsCompatibleWindowTransparentEnabled)
            return;
        var screen = GetDockingScreen(Settings.WindowDockingMonitorIndex);
        if (screen == null)
            return;
        // 水波纹中心取岛内容中心，转成相对顶层特效窗口（=工作区原点）的 DIP，复用 MainWindow 的实现。
        var workArea = screen.WorkingArea;
        var centerX = (_windowLeftPx + _contentWidthPx / 2.0 - workArea.Left) / _dpiX;
        var centerY = (_windowTopPx + _contentHeightPx / 2.0 - workArea.Top) / _dpiY;
        _topmostEffectWindow.Dispatcher.Invoke(() =>
        {
            _topmostEffectWindow.UpdateWindowPos(screen, 1 / _dpiX);
            _topmostEffectWindow.PlayEffect(new RippleEffect
            {
                CenterX = centerX,
                CenterY = centerY
            });
        });
    }

    // ponytail: 自绘岛暂无 overlay 倒计时条，进度回调留空。
    public void OnNotificationProgressStarted(TimeSpan duration)
    {
    }

    public void OnNotificationProgressStopped()
    {
    }

    #endregion

    private void StartNotificationAnimation(string name)
    {
        if (!AnimSpecsByStyle.TryGetValue(Settings.IslandMaskAnimationStyle, out var specs) ||
            !specs.TryGetValue(name, out var spec))
            return;

        // OverlayOut 会和遮罩收合（OverlayMaskOut / OverlayMaskOutDirect）在同一帧触发，
        // 单 _animSpec 会被覆盖导致收合动画丢失；把仍在跑的遮罩字段并入新 spec。
        var ongoing = _animSpec;
        var ongoingHasMask = ongoing is not null && (ongoing.MaskRegions is not null || ongoing.MaskY is not null);
        var incomingHasMask = spec.MaskRegions is not null || spec.MaskY is not null;
        if (ongoingHasMask && !incomingHasMask)
        {
            spec = new AnimSpec
            {
                Duration = Math.Max(spec.Duration, ongoing!.Duration),
                MaskY = ongoing.MaskY,
                MaskContentOpacity = ongoing.MaskContentOpacity,
                MaskContentScale = ongoing.MaskContentScale,
                MaskRegions = ongoing.MaskRegions,
                OverlayOpacity = spec.OverlayOpacity,
                ContentOpacity = spec.ContentOpacity
            };
        }

        _animSpec = spec;
        _animStart = null;
        EnsureRenderingHook();
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (e is not RenderingEventArgs args)
        {
            StopRenderingHook();
            return;
        }

        var active = false;

        if (_animSpec is { } spec)
        {
            _animStart ??= args.RenderingTime;
            var t = (args.RenderingTime - _animStart.Value).TotalSeconds;
            if (spec.MaskY is not null)
                _renderer.MaskOffsetY = Eval(spec.MaskY, t);
            if (spec.MaskContentOpacity is not null)
                _renderer.MaskContentOpacity = Eval(spec.MaskContentOpacity, t);
            if (spec.MaskContentScale is not null)
                _renderer.MaskContentScale = Eval(spec.MaskContentScale, t);
            if (spec.OverlayOpacity is not null)
                _renderer.OverlayOpacity = Eval(spec.OverlayOpacity, t);
            if (spec.ContentOpacity is not null)
                _renderer.ContentOpacity = Eval(spec.ContentOpacity, t);
            if (spec.MaskRegions is not null)
            {
                for (var i = 0; i < 5; i++)
                    _renderer.MaskRegionProgress[i] = Eval(spec.MaskRegions[i], t);
            }
            if (t >= spec.Duration)
            {
                _animSpec = null;
                _animStart = null;
            }
            else
            {
                active = true;
            }
        }

        if (_maskClosing && _animSpec is null)
            FinalizeNotificationVisual();

        if (_showAnimActive)
        {
            _showAnimStart ??= args.RenderingTime;
            var t = (args.RenderingTime - _showAnimStart.Value).TotalSeconds;
            var p = t / ShowAnimatedDuration;
            if (p >= 1)
            {
                ApplyShowVisual(1, 1);
                _showAnimActive = false;
                _showAnimStart = null;
            }
            else
            {
                ApplyShowVisual(
                    _showFromScale + (1 - _showFromScale) * OutElasticHalf(p),
                    _showFromOpacity + (1 - _showFromOpacity) * OutExpo(Math.Min(1, p / ShowFadePortion)));
                active = true;
            }
        }

        if (_hideAnimActive)
        {
            _hideAnimStart ??= args.RenderingTime;
            var t = (args.RenderingTime - _hideAnimStart.Value).TotalSeconds;
            var p = t / HideAnimatedDuration;
            if (p >= 1)
            {
                ApplyShowVisual(ShowScaleMin, 0);
                _hideAnimActive = false;
                _hideAnimStart = null;
                ShowWindow((HWND)_hwnd, SHOW_WINDOW_CMD.SW_HIDE);
            }
            else
            {
                ApplyShowVisual(
                    _hideFromScale + (ShowScaleMin - _hideFromScale) * OutExpo(p),
                    _hideFromOpacity * (1 - CubicOut(p)));
                active = true;
            }
        }

        if (_renderer.TickFades(args.RenderingTime.TotalSeconds))
            active = true;

        _renderer.Invalidate();

        if (!active)
            StopRenderingHook();
    }

    private void EnsureRenderingHook()
    {
        if (_renderingHooked)
            return;
        CompositionTarget.Rendering += OnRendering;
        _renderingHooked = true;
    }

    private void StopRenderingHook()
    {
        if (!_renderingHooked)
            return;
        CompositionTarget.Rendering -= OnRendering;
        _renderingHooked = false;
    }

    private void ApplyShowVisual(double scale, double opacity)
    {
        if (_surface.RenderTransform is ScaleTransform st)
        {
            st.ScaleX = scale;
            st.ScaleY = scale;
        }

        _surface.Opacity = opacity;
    }

    private static double OutElasticHalf(double t)
    {
        t = Math.Max(0, Math.Min(1, t));
        return Math.Pow(2, -10 * t) * Math.Sin((0.5 * t - ElasticConst2) * ElasticConst) + 1 -
               ElasticOffsetHalf * t;
    }

    private static double OutExpo(double t)
    {
        t = Math.Max(0, Math.Min(1, t));
        return -Math.Pow(2, -10 * t) + 1 + ExpoOffset * t;
    }

    private static double CubicOut(double t)
    {
        t = Math.Max(0, Math.Min(1, t));
        var u = 1 - t;
        return 1 - u * u * u;
    }

    /// <summary>当前动画视觉态（scale/opacity），用于打断时从当前位置续接。</summary>
    private (double Scale, double Opacity) GetVisual()
    {
        var scale = _surface.RenderTransform is ScaleTransform st ? st.ScaleX : 1.0;
        return (scale, _surface.Opacity);
    }

    private static double Eval((double T, double V, IEasingFunction? E)[] keys, double t)
    {
        if (t <= keys[0].T)
            return keys[0].V;
        for (var i = 1; i < keys.Length; i++)
        {
            if (t > keys[i].T)
                continue;
            var a = keys[i - 1];
            var b = keys[i];
            var span = b.T - a.T;
            var u = span <= 0 ? 1 : (t - a.T) / span;
            u = b.E?.Ease(u) ?? u;
            return a.V + (b.V - a.V) * u;
        }

        return keys[^1].V;
    }

    private static Dictionary<int, Dictionary<string, AnimSpec>> BuildAllAnimSpecs() => new()
    {
        [0] = BuildClassicIsland1Specs(),
        [1] = BuildClassicIsland2Specs(),
        [2] = BuildFluentIsland2Specs()
    };

    /// <summary>ClassIsland 1：矩形遮罩块滑入/滑出，文字延迟淡入（QuinticEaseOut / CircleEaseIn）。</summary>
    private static Dictionary<string, AnimSpec> BuildClassicIsland1Specs()
    {
        var quintOut = new QuinticEase { EasingMode = EasingMode.EaseOut };
        var powerIn = new PowerEase { EasingMode = EasingMode.EaseIn };
        var circleIn = new CircleEase { EasingMode = EasingMode.EaseIn };
        var cubicOut = new CubicEase { EasingMode = EasingMode.EaseOut };
        return new Dictionary<string, AnimSpec>
        {
            ["OverlayMaskIn"] = new()
            {
                Duration = 0.3,
                MaskY = [(0, -60, null), (0.2, 0, quintOut)],
                MaskContentOpacity = [(0, 0, null), (0.2, 0, null), (0.3, 1, powerIn)],
                OverlayOpacity = [(0.2, 0, null), (0.3, 1, quintOut)]
            },
            ["OverlayMaskOut"] = new()
            {
                Duration = 0.3,
                MaskY = [(0, 0, null), (0.1, 0, null), (0.3, 60, circleIn)],
                MaskContentOpacity = [(0, 1, null), (0.1, 0, null)],
                OverlayOpacity = [(0, 1, null)],
                ContentOpacity = [(0, 0, null)]
            },
            ["OverlayOut"] = new()
            {
                Duration = 0.3,
                OverlayOpacity = [(0, 1, null), (0.1, 1, null), (0.3, 0, circleIn)],
                ContentOpacity = [(0, 0, null), (0.3, 1, null)]
            },
            ["OverlayMaskOutDirect"] = new()
            {
                Duration = 0.3,
                MaskY = [(0, 0, null), (0.1, 0, null), (0.3, 60, circleIn)],
                MaskContentOpacity = [(0, 1, null), (0.1, 0, null)],
                OverlayOpacity = [(0, 1, null), (0.1, 0, cubicOut)],
                ContentOpacity = [(0, 0, null), (0.3, 1, null)]
            }
        };
    }

    /// <summary>ClassIsland 2 默认（ClassicTheme）：矩形遮罩块滑入/滑出，缓动方向与 CI1 相反。</summary>
    private static Dictionary<string, AnimSpec> BuildClassicIsland2Specs()
    {
        var quadIn = new QuadraticEase { EasingMode = EasingMode.EaseIn };
        var quadOut = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        var sineIn = new SineEase { EasingMode = EasingMode.EaseIn };
        var circleIn = new CircleEase { EasingMode = EasingMode.EaseIn };
        var cubicOut = new CubicEase { EasingMode = EasingMode.EaseOut };
        return new Dictionary<string, AnimSpec>
        {
            ["OverlayMaskIn"] = new()
            {
                Duration = 0.3,
                MaskY = [(0, -50, null), (0.2, 0, quadIn)],
                MaskContentOpacity = [(0, 0, null), (0.2, 0, null), (0.3, 1, null)],
                OverlayOpacity = [(0.2, 0, null), (0.3, 1, quadOut)],
                ContentOpacity = [(0, 1, null), (0.2, 0, sineIn)]
            },
            ["OverlayMaskOut"] = new()
            {
                Duration = 0.3,
                MaskY = [(0, 0, null), (0.1, 0, null), (0.3, 50, quadOut)],
                MaskContentOpacity = [(0, 1, null), (0.1, 0, null)],
                OverlayOpacity = [(0, 1, null)],
                ContentOpacity = [(0, 0, null)]
            },
            ["OverlayOut"] = new()
            {
                Duration = 0.3,
                OverlayOpacity = [(0, 1, null), (0.1, 1, null), (0.3, 0, circleIn)],
                ContentOpacity = [(0, 0, null), (0.3, 1, null)]
            },
            ["OverlayMaskOutDirect"] = new()
            {
                Duration = 0.3,
                MaskY = [(0, 0, null), (0.1, 0, null), (0.3, 50, quadOut)],
                MaskContentOpacity = [(0, 1, null), (0.1, 0, null)],
                OverlayOpacity = [(0, 1, null), (0.1, 0, cubicOut)],
                ContentOpacity = [(0, 0, null), (0.3, 1, null)]
            }
        };
    }

    /// <summary>ClassIsland 2 Fluent：5 个平行四边形遮罩，外→内展开 / 内→外收合，文字延迟淡入 + 缩放。</summary>
    private static Dictionary<string, AnimSpec> BuildFluentIsland2Specs()
    {
        var quintOut = new QuinticEase { EasingMode = EasingMode.EaseOut };
        var circleIn = new CircleEase { EasingMode = EasingMode.EaseIn };
        var cubicOut = new CubicEase { EasingMode = EasingMode.EaseOut };
        var openRegions = BuildSlantedRegions(open: true);
        var closeRegions = BuildSlantedRegions(open: false);
        return new Dictionary<string, AnimSpec>
        {
            ["OverlayMaskIn"] = new()
            {
                Duration = 1.01,
                MaskRegions = openRegions,
                MaskContentOpacity = [(0.26, 0, null), (0.51, 1, quintOut)],
                MaskContentScale = [(0.26, 1.1, null), (1.01, 1.0, quintOut)],
                OverlayOpacity = [(0, 0, null)],
                ContentOpacity = [(0, 0, null)]
            },
            ["OverlayMaskOut"] = new()
            {
                Duration = 0.48,
                MaskRegions = closeRegions,
                MaskContentOpacity = [(0, 1, null), (0.2, 0, null)],
                MaskContentScale = [(0, 1.0, null), (0.2, 1.1, null)],
                OverlayOpacity = [(0, 1, null)],
                ContentOpacity = [(0, 0, null)]
            },
            ["OverlayOut"] = new()
            {
                Duration = 0.3,
                OverlayOpacity = [(0, 1, null), (0.1, 1, null), (0.3, 0, circleIn)],
                ContentOpacity = [(0, 0, null), (0.3, 1, null)]
            },
            ["OverlayMaskOutDirect"] = new()
            {
                Duration = 0.48,
                MaskRegions = closeRegions,
                MaskContentOpacity = [(0, 1, null), (0.2, 0, null)],
                MaskContentScale = [(0, 1.0, null), (0.2, 1.1, null)],
                OverlayOpacity = [(0, 1, null)],
                ContentOpacity = [(0, 0, null), (0.48, 1, null)]
            }
        };
    }

    /// <summary>构建 Fluent 平行四边形遮罩 5 个分区的关键帧（外→内 or 内→外，带 stagger）。</summary>
    private static (double T, double V, IEasingFunction? E)[][] BuildSlantedRegions(bool open)
    {
        const double duration = 0.36;
        IEasingFunction easing = open
            ? new QuarticEase { EasingMode = EasingMode.EaseOut }
            : new QuadraticEase { EasingMode = EasingMode.EaseIn };
        double[] delays = open ? [0, 0.09, 0.12, 0.09, 0] : [0.12, 0.09, 0, 0.09, 0.12];
        var from = open ? 0.0 : 1.0;
        var to = open ? 1.0 : 0.0;
        var regions = new (double, double, IEasingFunction?)[5][];
        for (var i = 0; i < 5; i++)
            regions[i] = [(delays[i], from, null), (delays[i] + duration, to, easing)];
        return regions;
    }

    private sealed class AnimSpec
    {
        public double Duration;
        public (double T, double V, IEasingFunction? E)[]? MaskY;
        public (double T, double V, IEasingFunction? E)[]? MaskContentOpacity;
        public (double T, double V, IEasingFunction? E)[]? MaskContentScale;
        public (double T, double V, IEasingFunction? E)[]? OverlayOpacity;
        public (double T, double V, IEasingFunction? E)[]? ContentOpacity;
        public (double T, double V, IEasingFunction? E)[][]? MaskRegions;
    }

    private void UpdateMaskVisual(NotificationContent? content)
    {
        switch (content?.Content)
        {
            case TwoIconsMaskTemplateData data:
                _renderer.MaskText = data.Text;
                _renderer.MaskLeftIcon = RemixIconGlyph.Get(PackIconRemixIconKind.ErrorWarningLine);
                _renderer.MaskRightIcon = data.HasRightIcon ? RemixIconGlyph.Get(PackIconRemixIconKind.BookOpenLine) : "";
                break;
            case SimpleTextTemplateData data:
                _renderer.MaskText = data.Text ?? "";
                _renderer.MaskLeftIcon = "";
                _renderer.MaskRightIcon = "";
                break;
            case ClassNotificationProviderControl:
                _renderer.MaskText = _lessonsService.CurrentTimeLayoutItem.BreakNameText;
                _renderer.MaskLeftIcon = RemixIconGlyph.Get(PackIconRemixIconKind.ErrorWarningLine);
                _renderer.MaskRightIcon = RemixIconGlyph.Get(PackIconRemixIconKind.TimeLine);
                break;
            default:
                _renderer.MaskText = "";
                _renderer.MaskLeftIcon = "";
                _renderer.MaskRightIcon = "";
                break;
        }
    }

    private string ExtractOverlayText(NotificationContent? content)
    {
        if (content?.Content is not ClassNotificationProviderControl control)
            return "";

        var next = _lessonsService.NextClassSubject;
        var teacher = control.ShowTeacherName ? next.TeacherName : "";
        var time =
            $"{_lessonsService.NextClassTimeLayoutItem.StartTime:hh\\:mm}-{_lessonsService.NextClassTimeLayoutItem.EndTime:hh\\:mm}";
        var nextInfo = $"下节课是：{next.Name} {teacher} {time}".Trim();
        return control.Mode switch
        {
            "ClassPrepareNotifyOverlay" =>
                $"距上课还剩 {_lessonsService.OnClassLeftTime.TotalSeconds:F0} 秒    {nextInfo}",
            "ClassOffOverlay" =>
                $"本节{_lessonsService.CurrentTimeLayoutItem.BreakNameText}长 {ClassNotificationProviderControl.FormatTimeSpan(_lessonsService.CurrentTimeLayoutItem.Last)}    {nextInfo}",
            _ => control.Message
        };
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        RefreshTheme();
        UpdateWindowPos();
        ApplyWindowStyles();
        UpdateVisibility();
        if (e.PropertyName == nameof(Settings.IslandMaskAnimationStyle))
            UpdateNotification();
        if (_renderer.UpdateFadeTargets())
            EnsureRenderingHook();
    }

    private void OnTopmostRecheckTick(object? sender, EventArgs e)
    {
        ReCheckTopmostState();
        SetBottom();
    }

    private void OnForegroundWindowChanged(HWINEVENTHOOK hWinEventHook, uint @event, HWND hwnd, int idObject,
        int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (Settings.WindowTopmostRecheckMode == 1)
        {
            ReCheckTopmostState();
            SetBottom();
        }
    }

    private void OnThemeUpdated(object? sender, ThemeUpdatedEventArgs e) => RefreshTheme();

    private void OnRendererInvalidated(object? sender, EventArgs e) => UpdateWindowPos();

    /// <summary>把主题色、前景色、主题背景色同步给渲染上下文，并重建画笔。</summary>
    private void RefreshTheme()
    {
        var settings = Settings;
        _context.InvalidateFont();
        _context.AccentColor = _themeService.PrimaryColor;
        _context.ForegroundColor = settings.IsCustomForegroundColorEnabled
            ? settings.CustomForegroundColor
            : ResolveThemeColor("MahApps.Brushes.ThemeForeground", Colors.White);
        _context.ThemeBackground = ResolveThemeColor("MahApps.Brushes.ThemeBackground", Color.FromRgb(0x1F, 0x1F, 0x1F));
        _renderer.RefreshStyles();
        _renderer.Invalidate();
    }

    private static Color ResolveThemeColor(string key, Color fallback)
        => Application.Current?.TryFindResource(key) is SolidColorBrush brush ? brush.Color : fallback;

    private void UpdateDpi()
    {
        if (_source?.CompositionTarget is { } target)
        {
            _dpiX = target.TransformToDevice.M11;
            _dpiY = target.TransformToDevice.M22;
        }

        _context.PixelsPerDip = _dpiX;
    }

    private static System.Windows.Forms.Screen? GetDockingScreen(int index)
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        return index >= 0 && index < screens.Length ? screens[index] : System.Windows.Forms.Screen.PrimaryScreen;
    }

    /// <summary>
    ///     按停靠位置返回内容对齐（同时作为出现动画缩放锚点）：0=左/上，0.5=中，1=右/下。
    ///     贴边时岛从贴边一侧向屏幕内生长，弹性过冲不越出屏幕。
    /// </summary>
    private (double H, double V) GetDockingAlign() => Settings.WindowDockingLocation switch
    {
        1 => (0.5, 0.0), // 中上
        2 => (1.0, 0.0), // 右上
        3 => (0.0, 1.0), // 左下
        4 => (0.5, 1.0), // 中下
        5 => (1.0, 1.0), // 右下
        _ => (0.0, 0.0)  // 左上
    };

    /// <summary>
    ///     按内容重新计算窗口大小与停靠位置（等价 MainWindow.UpdateWindowPos，窗口宽度取内容宽度）。
    ///     窗口恒定按 <see cref="IslandSurface.WindowOvershootScale"/> 预留放大；预留全部放在朝向屏幕内的一侧
    ///     （对齐锚点 = 停靠边），内容按锚点贴向停靠边。这样出现动画从贴边侧向屏幕内生长，
    ///     弹性过冲峰值始终完整落在屏幕内，且窗口尺寸不随动画变化。
    /// </summary>
    private void UpdateWindowPos()
    {
        if (_source == null)
            return;

        var screen = GetDockingScreen(Settings.WindowDockingMonitorIndex);
        if (screen == null)
            return;

        var (hAlign, vAlign) = GetDockingAlign();
        _surface.HorizontalAlign = hAlign;
        _surface.VerticalAlign = vAlign;
        _surface.RenderTransformOrigin = new Point(hAlign, vAlign);

        var overshoot = _surface.WindowOvershootScale <= 0 ? 1.0 : _surface.WindowOvershootScale;
        var available = new Size(screen.WorkingArea.Width / _dpiX, double.PositiveInfinity);
        var reserved = _surface.MeasureContent(available);
        var contentWidth = reserved.Width / overshoot;
        var contentHeight = reserved.Height / overshoot;

        var widthPx = (int)Math.Ceiling(reserved.Width * _dpiX);
        var heightPx = (int)Math.Ceiling(reserved.Height * _dpiY);
        var contentWidthPx = (int)Math.Ceiling(contentWidth * _dpiX);
        var contentHeightPx = (int)Math.Ceiling(contentHeight * _dpiY);
        if (widthPx <= 0 || heightPx <= 0)
            return;

        _contentWidthPx = contentWidthPx;
        _contentHeightPx = contentHeightPx;

        var offsetAreaTop = Settings.IsIgnoreWorkAreaEnabled ? screen.Bounds.Top : screen.WorkingArea.Top;
        var offsetAreaBottom = Settings.IsIgnoreWorkAreaEnabled ? screen.Bounds.Bottom : screen.WorkingArea.Bottom;
        var centerX = (double)(screen.WorkingArea.Left + screen.WorkingArea.Right) / 2;

        double left = screen.WorkingArea.Left, top = offsetAreaTop;
        switch (Settings.WindowDockingLocation)
        {
            case 1: // 中上
                left = centerX - (double)contentWidthPx / 2;
                break;
            case 2: // 右上
                left = screen.WorkingArea.Right - contentWidthPx;
                break;
            case 3: // 左下
                top = offsetAreaBottom - contentHeightPx;
                break;
            case 4: // 中下
                left = centerX - (double)contentWidthPx / 2;
                top = offsetAreaBottom - contentHeightPx;
                break;
            case 5: // 右下
                left = screen.WorkingArea.Right - contentWidthPx;
                top = offsetAreaBottom - contentHeightPx;
                break;
        }

        left += Settings.WindowDockingOffsetX;
        top += Settings.WindowDockingOffsetY;

        // 内容锚点（供命中测试用）保持为内容左上角；窗口左上角再按对齐把预留推到屏幕内一侧。
        var padX = (widthPx - contentWidthPx) * hAlign;
        var padY = (heightPx - contentHeightPx) * vAlign;
        _windowLeftPx = (int)Math.Round(left);
        _windowTopPx = (int)Math.Round(top);
        SetWindowPos((HWND)_hwnd, HWND.Null, (int)Math.Round(left - padX), (int)Math.Round(top - padY),
            widthPx, heightPx, SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);

        _surface.Redraw(); // 对齐/尺寸变化后立即按新锚点重绘
    }

    /// <summary>按隐藏规则（可见性、上课/全屏/最大化/规则集）决定是否显示窗口。</summary>
    private void UpdateVisibility()
    {
        if (_source == null)
            return;

        var settings = Settings;
        var visible = settings.IsMainWindowVisible && settings.UseSelfDrawnIsland;
        if (visible)
        {
            switch (settings.HideMode)
            {
                case 0:
                    if (settings.HideOnClass && _lessonsService.CurrentState == TimeState.OnClass)
                        visible = false;
                    else if (settings.HideOnMaxWindow && IsForegroundMaxWindow())
                        visible = false;
                    else if (settings.HideOnFullscreen && IsForegroundFullscreen())
                        visible = false;
                    break;
                case 1:
                    visible = !_conditionEvaluator.Evaluate(settings.HideCondition);
                    break;
            }
        }

        SetVisible(visible);
    }

    private void SetVisible(bool visible)
    {
        if (_source == null || _isVisible == visible)
            return;
        _isVisible = visible;
        if (visible)
        {
            UpdateWindowPos();
            ApplyWindowStyles();
            if (Settings.IsIslandShowAnimationEnabled)
            {
                if (_hideAnimActive)
                {
                    // 隐藏动画中被打断 → 从当前视觉续接
                    (_showFromScale, _showFromOpacity) = GetVisual();
                }
                else
                {
                    // 新鲜出现：先落到起点，避免全尺寸闪一帧
                    _showFromScale = ShowScaleMin;
                    _showFromOpacity = 0;
                    ApplyShowVisual(_showFromScale, _showFromOpacity);
                }

                _hideAnimActive = false;
                _hideAnimStart = null;
                _showAnimActive = true;
                _showAnimStart = null;
            }
            else
            {
                _showAnimActive = false;
                _showAnimStart = null;
                _hideAnimActive = false;
                _hideAnimStart = null;
                ApplyShowVisual(1, 1);
            }

            ShowWindow((HWND)_hwnd, SHOW_WINDOW_CMD.SW_SHOWNOACTIVATE);
            if (_showAnimActive)
                EnsureRenderingHook();
        }
        else
        {
            if (Settings.IsIslandHideAnimationEnabled)
            {
                // 出现动画中被打断 → 从当前视觉续接；否则从静止态收起
                (_hideFromScale, _hideFromOpacity) = _showAnimActive ? GetVisual() : (1.0, 1.0);
                _showAnimActive = false;
                _showAnimStart = null;
                _hideAnimActive = true;
                _hideAnimStart = null;
                EnsureRenderingHook();
            }
            else
            {
                _showAnimActive = false;
                _showAnimStart = null;
                _hideAnimActive = false;
                _hideAnimStart = null;
                ShowWindow((HWND)_hwnd, SHOW_WINDOW_CMD.SW_HIDE);
            }
        }
    }

    private bool IsForegroundFullscreen()
    {
        var screen = GetDockingScreen(Settings.WindowDockingMonitorIndex);
        return screen != null && NativeWindowHelper.IsForegroundFullScreen(screen);
    }

    private bool IsForegroundMaxWindow()
    {
        var screen = GetDockingScreen(Settings.WindowDockingMonitorIndex);
        return screen != null && NativeWindowHelper.IsForegroundMaxWindow(screen);
    }

    /// <summary>按光标位置更新鼠标所在行，驱动悬停淡出。</summary>
    private void UpdateMouseIn()
    {
        if (_source == null || !GetCursorPos(out var cursor))
            return;

        var line = HitTestLine(cursor.X, cursor.Y);
        if (line == _renderer.MouseInLine)
            return;
        _renderer.MouseInLine = line;
        if (_renderer.UpdateFadeTargets())
            EnsureRenderingHook();
        _renderer.Invalidate();
    }

    private int HitTestLine(int screenX, int screenY)
    {
        var scale = Settings.Scale <= 0 ? 1.0 : Settings.Scale;
        var x = (screenX - _windowLeftPx) / (scale * _dpiX);
        var y = (screenY - _windowTopPx) / (scale * _dpiY);
        return _renderer.HitTestLine(new Point(x, y));
    }

    /// <summary>应用扩展样式：工具窗、点击穿透、防截屏、置顶置底与层级复查。</summary>
    private void ApplyWindowStyles()
    {
        if (_source == null)
            return;

        var hwnd = (HWND)_hwnd;
        var style = GetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        if (Settings.IsScreenRecordingModeEnabled)
            style &= ~NativeWindowHelper.WS_EX_TOOLWINDOW;
        else
            style |= NativeWindowHelper.WS_EX_TOOLWINDOW;

        if (Settings.IsMouseClickingEnabled)
            style &= ~NativeWindowHelper.WS_EX_TRANSPARENT;
        else
            style |= NativeWindowHelper.WS_EX_TRANSPARENT;

        SetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, style);

        SetWindowDisplayAffinity(hwnd, Settings.IsWindowCaptureBlockingEnabled
            ? WINDOW_DISPLAY_AFFINITY.WDA_EXCLUDEFROMCAPTURE
            : WINDOW_DISPLAY_AFFINITY.WDA_NONE);

        UpdateLayer();
        UpdateTopmostRecheckTimer();
    }

    private void UpdateLayer()
    {
        if (Settings.WindowLayer == 1)
            ReCheckTopmostState();
        else
            SetBottom();
    }

    private void ReCheckTopmostState()
    {
        if (_source == null)
            return;
        if (!_viewModel.IsNotificationWindowExplicitShowed && Settings.WindowLayer != 1)
            return;
        SetWindowPos((HWND)_hwnd, NativeWindowHelper.HWND_TOPMOST, 0, 0, 0, 0,
            SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOMOVE |
            SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_NOSENDCHANGING);
    }

    private void SetBottom()
    {
        if (_source == null || Settings.WindowLayer != 0)
            return;
        if (_viewModel.IsNotificationWindowExplicitShowed)
            return;
        SetWindowPos((HWND)_hwnd, NativeWindowHelper.HWND_BOTTOM, 0, 0, 0, 0,
            SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOMOVE |
            SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
    }

    private void UpdateTopmostRecheckTimer()
    {
        _topmostRecheckTimer.Stop();
        var interval = Settings.WindowTopmostRecheckMode switch
        {
            2 => 1000d,
            3 => 500d,
            4 => 200d,
            5 => 100d,
            6 => 50d,
            7 => 1d,
            8 => Math.Max(1d, Settings.WindowTopmostRecheckIntervalMs),
            _ => 0d
        };
        if (interval <= 0)
            return;
        _topmostRecheckTimer.Interval = TimeSpan.FromMilliseconds(interval);
        _topmostRecheckTimer.Start();
    }
}
