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
using Linearstar.Windows.RawInput;
using MahApps.Metro.IconPacks;
using Windows.Win32.UI.Accessibility;
using Windows.Win32.UI.Controls;

namespace ClassIsland.Controls.Island;

/// <summary>
/// 岛的纯自绘宿主。基于裸 <see cref="HwndSource"/>，无 XAML、无 <see cref="Window"/> 壳，
/// 内容为单个 <see cref="DrawingVisual"/>。
/// </summary>
public sealed class IslandHost : IDisposable
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
    private readonly ClassIsland.MainWindow _mainWindow;
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
    private bool _isVisible;
    private bool _forceLayered;

    private AnimSpec? _animSpec;
    private TimeSpan? _animStart;
    private bool _renderingHooked;
    private bool _showAnimActive;
    private TimeSpan? _showAnimStart;
    private static readonly Dictionary<string, AnimSpec> AnimSpecs = BuildAnimSpecs();

    private const double ShowScaleMin = 0.89;
    private const double ShowAnimatedDuration = 0.47;
    private const double ShowFadePortion = 0.78;
    private const double ElasticConst = 2 * Math.PI / 0.3;
    private const double ElasticConst2 = 0.3 / 4;
    private static readonly double ExpoOffset = Math.Pow(2, -10);
    private static readonly double ElasticOffsetHalf =
        Math.Pow(2, -10) * Math.Sin((0.5 - ElasticConst2) * ElasticConst);

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
        ClassIsland.MainWindow mainWindow)
    {
        _settingsService = settingsService;
        _themeService = themeService;
        _lessonsService = lessonsService;
        _componentsService = componentsService;
        _weatherService = weatherService;
        _windowRuleService = windowRuleService;
        _rulesetService = rulesetService;
        _mainWindow = mainWindow;

        _context = new IslandContext(settingsService.Settings, lessonsService, profileService, exactTimeService,
            rulesetService, weatherService)
        {
            AccentColor = themeService.PrimaryColor
        };
        _renderer = new IslandRenderer(_context);
        _surface = new IslandSurface(_renderer);

        BuildComponents();
        _componentsService.CurrentComponents.CollectionChanged += OnComponentsChanged;
        _componentsService.PropertyChanged += OnComponentsServicePropertyChanged;

        _topmostRecheckTimer.Tick += OnTopmostRecheckTick;
        _windowRuleService.ForegroundWindowChanged += OnForegroundWindowChanged;
        _rulesetService.StatusUpdated += OnRulesetStatusUpdated;
        _mainWindow.ViewModel.PropertyChanged += OnNotificationChanged;
        _mainWindow.MainWindowAnimationEvent += OnMainWindowAnimation;
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
        _componentsService.CurrentComponents.CollectionChanged -= OnComponentsChanged;
        _componentsService.PropertyChanged -= OnComponentsServicePropertyChanged;
        _settingsService.Settings.PropertyChanged -= OnSettingsChanged;
        _themeService.ThemeUpdated -= OnThemeUpdated;
        _lessonsService.PostMainTimerTicked -= OnLessonsTicked;
        _lessonsService.CurrentTimeStateChanged -= OnLessonsTicked;
        _windowRuleService.ForegroundWindowChanged -= OnForegroundWindowChanged;
        _rulesetService.StatusUpdated -= OnRulesetStatusUpdated;
        _mainWindow.ViewModel.PropertyChanged -= OnNotificationChanged;
        _mainWindow.MainWindowAnimationEvent -= OnMainWindowAnimation;
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
        foreach (var settings in _componentsService.CurrentComponents)
        {
            var component = IslandComponentFactory.Create(settings, _context);
            if (component != null)
                _renderer.Add(component);
        }
    }

    private void OnComponentsChanged(object? sender, NotifyCollectionChangedEventArgs e) => BuildComponents();

    private void OnComponentsServicePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IComponentsService.CurrentComponents))
        {
            _componentsService.CurrentComponents.CollectionChanged -= OnComponentsChanged;
            _componentsService.CurrentComponents.CollectionChanged += OnComponentsChanged;
            BuildComponents();
        }
    }

    private void OnNotificationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is "CurrentMaskContent" or "CurrentOverlayContent")
            UpdateNotification();
    }

    /// <summary>从 MainWindow 的提醒状态提取自绘所需的遮罩 / overlay 文本。</summary>
    private void UpdateNotification()
    {
        var viewModel = _mainWindow.ViewModel;
        var mask = viewModel.CurrentMaskContent;
        if (mask != null && !_renderer.IsMaskVisible)
        {
            // 新遮罩出现：复位到滑入起点，等 OverlayMaskIn 驱动。
            _renderer.MaskOffsetY = -60;
            _renderer.MaskOpacity = 0;
            _renderer.ContentOpacity = 1;
        }

        _renderer.IsMaskVisible = mask != null;
        UpdateMaskVisual(mask);
        var overlay = viewModel.CurrentOverlayContent;
        _renderer.IsOverlayVisible = overlay != null;
        _renderer.OverlayText = ExtractOverlayText(overlay);

        if (mask == null && overlay == null)
        {
            // 提醒结束：复位并停止逐帧动画。
            _renderer.MaskOffsetY = 0;
            _renderer.MaskOpacity = 1;
            _renderer.OverlayOpacity = 1;
            _renderer.ContentOpacity = 1;
            _animSpec = null;
            _animStart = null;
        }

        _renderer.Invalidate();
    }

    private void OnMainWindowAnimation(object? sender, MainWindowAnimationEventArgs e)
        => StartNotificationAnimation(e.StoryboardName);

    private void StartNotificationAnimation(string name)
    {
        if (!AnimSpecs.TryGetValue(name, out var spec))
            return;
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
            if (spec.MaskOpacity is not null)
                _renderer.MaskOpacity = Eval(spec.MaskOpacity, t);
            if (spec.OverlayOpacity is not null)
                _renderer.OverlayOpacity = Eval(spec.OverlayOpacity, t);
            if (spec.ContentOpacity is not null)
                _renderer.ContentOpacity = Eval(spec.ContentOpacity, t);
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
                    ShowScaleMin + (1 - ShowScaleMin) * OutElasticHalf(p),
                    OutExpo(Math.Min(1, p / ShowFadePortion)));
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

    private static Dictionary<string, AnimSpec> BuildAnimSpecs()
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
                MaskOpacity = [(0, 0, null), (0.2, 0, null), (0.3, 1, powerIn)],
                OverlayOpacity = [(0.2, 0, null), (0.3, 1, quintOut)]
            },
            ["OverlayMaskOut"] = new()
            {
                Duration = 0.3,
                MaskY = [(0, 0, null), (0.1, 0, null), (0.3, 60, circleIn)],
                MaskOpacity = [(0, 1, null), (0.1, 0, null)],
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
                MaskOpacity = [(0, 1, null), (0.1, 0, null)],
                OverlayOpacity = [(0, 1, null), (0.1, 0, cubicOut)],
                ContentOpacity = [(0, 0, null), (0.3, 1, null)]
            }
        };
    }

    private sealed class AnimSpec
    {
        public double Duration;
        public (double T, double V, IEasingFunction? E)[]? MaskY;
        public (double T, double V, IEasingFunction? E)[]? MaskOpacity;
        public (double T, double V, IEasingFunction? E)[]? OverlayOpacity;
        public (double T, double V, IEasingFunction? E)[]? ContentOpacity;
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
            $"{_lessonsService.NextClassTimeLayoutItem.StartSecond:HH:mm}-{_lessonsService.NextClassTimeLayoutItem.EndSecond:HH:mm}";
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

    /// <summary>按内容重新计算窗口大小与停靠位置（等价 MainWindow.UpdateWindowPos，窗口宽度取内容宽度）。</summary>
    private void UpdateWindowPos()
    {
        if (_source == null)
            return;

        var screen = GetDockingScreen(Settings.WindowDockingMonitorIndex);
        if (screen == null)
            return;

        var available = new Size(screen.WorkingArea.Width / _dpiX, double.PositiveInfinity);
        var desired = _surface.MeasureContent(available);
        var widthPx = (int)Math.Ceiling(desired.Width * _dpiX);
        var heightPx = (int)Math.Ceiling(desired.Height * _dpiY);
        if (widthPx <= 0 || heightPx <= 0)
            return;

        var offsetAreaTop = Settings.IsIgnoreWorkAreaEnabled ? screen.Bounds.Top : screen.WorkingArea.Top;
        var offsetAreaBottom = Settings.IsIgnoreWorkAreaEnabled ? screen.Bounds.Bottom : screen.WorkingArea.Bottom;
        var centerX = (double)(screen.WorkingArea.Left + screen.WorkingArea.Right) / 2;

        double left = screen.WorkingArea.Left, top = offsetAreaTop;
        switch (Settings.WindowDockingLocation)
        {
            case 1: // 中上
                left = centerX - (double)widthPx / 2;
                break;
            case 2: // 右上
                left = screen.WorkingArea.Right - widthPx;
                break;
            case 3: // 左下
                top = offsetAreaBottom - heightPx;
                break;
            case 4: // 中下
                left = centerX - (double)widthPx / 2;
                top = offsetAreaBottom - heightPx;
                break;
            case 5: // 右下
                left = screen.WorkingArea.Right - widthPx;
                top = offsetAreaBottom - heightPx;
                break;
        }

        left += Settings.WindowDockingOffsetX;
        top += Settings.WindowDockingOffsetY;

        _windowLeftPx = (int)left;
        _windowTopPx = (int)top;
        SetWindowPos((HWND)_hwnd, HWND.Null, _windowLeftPx, _windowTopPx, widthPx, heightPx,
            SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
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
                    visible = !_rulesetService.IsRulesetSatisfied(settings.HiedRules);
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
            ShowWindow((HWND)_hwnd, SHOW_WINDOW_CMD.SW_SHOWNOACTIVATE);
            if (Settings.IsIslandShowAnimationEnabled)
            {
                ApplyShowVisual(ShowScaleMin, 0);
                _showAnimActive = true;
                _showAnimStart = null;
                EnsureRenderingHook();
            }
            else
            {
                ApplyShowVisual(1, 1);
                _showAnimActive = false;
                _showAnimStart = null;
            }
        }
        else
        {
            _showAnimActive = false;
            _showAnimStart = null;
            ShowWindow((HWND)_hwnd, SHOW_WINDOW_CMD.SW_HIDE);
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
        if (_source == null || Settings.WindowLayer != 1)
            return;
        SetWindowPos((HWND)_hwnd, NativeWindowHelper.HWND_TOPMOST, 0, 0, 0, 0,
            SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOMOVE |
            SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_NOSENDCHANGING);
    }

    private void SetBottom()
    {
        if (_source == null || Settings.WindowLayer != 0)
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
