using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Windows.Win32.UI.Accessibility;
using ClassIsland.Controls.NotificationEffects;
using ClassIsland.Core;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Abstractions.Services.Management;
using ClassIsland.Core.Abstractions.Services.SpeechService;
using ClassIsland.Core.Helpers.Native;
using ClassIsland.Core.Models.Notification;
using ClassIsland.Models.EventArgs;
using ClassIsland.Shared.Abstraction.Models;
using ClassIsland.Shared.Abstraction.Services;
using ClassIsland.Shared.Interfaces;
using ClassIsland.Shared.Models.Notification;
using ClassIsland.Services;
using ClassIsland.Shared;
using ClassIsland.ViewModels;
using ClassIsland.Views;

using Microsoft.Extensions.Logging;
using Microsoft.Win32;

using NAudio.Wave;
using Application = System.Windows.Application;
using Window = System.Windows.Window;
using NAudio.Wave.SampleProviders;
using Linearstar.Windows.RawInput;
using WindowChrome = System.Windows.Shell.WindowChrome;
using Point = System.Windows.Point;
using YamlDotNet.Core;



#if DEBUG
using JetBrains.Profiler.Api;
#endif

namespace ClassIsland;
/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window, INotificationVisualHost
{
    public static readonly ICommand TrayIconLeftClickedCommand = new RoutedCommand();

    public event EventHandler? StartupCompleted;

    public MainViewModel ViewModel
    {
        get;
        set;
    }

    private Storyboard NotificationProgressBar { get; set; } = new Storyboard();

    private SettingsService SettingsService
    {
        get;
    }

    private ITaskBarIconService TaskBarIconService
    {
        get;
    }

    private IThemeService ThemeService
    {
        get;
    }

    public INotificationHostService NotificationHostService
    {
        get;
    }

    public IProfileService ProfileService
    {
        get;
    }

    public ILessonsService LessonsService { get; }

    public TopmostEffectWindow TopmostEffectWindow { get; }

    private bool IsRunningCompatibleMode { get; set; } = false;

    private IExactTimeService ExactTimeService { get; }

    public ISpeechService SpeechService { get; }

    public IComponentsService ComponentsService { get; }

    private ILogger<MainWindow> Logger;

    private double _latestDpiX = 1.0;
    private double _latestDpiY = 1.0;

    private DispatcherTimer TouchInFadingTimer { get; set; } = new();

    private DispatcherTimer TopmostRecheckTimer { get; } = new();

    private Stopwatch RawInputUpdateStopWatch { get; } = new();

    public ClassChangingWindow? ClassChangingWindow { get; set; }
    
    private IUriNavigationService UriNavigationService { get; }
    public IRulesetService RulesetService { get; }
    public IWindowRuleService WindowRuleService { get; }
    public IManagementService ManagementService { get; }

    public event EventHandler<MousePosChangedEventArgs>? MousePosChanged;

    public event EventHandler<RawInputEventArgs>? RawInputEvent;

    private Point _centerPointCache = new Point(0, 0);


    public static readonly DependencyProperty BackgroundWidthProperty = DependencyProperty.Register(
        nameof(BackgroundWidth), typeof(double), typeof(MainWindow), new PropertyMetadata(0.0));

    public double BackgroundWidth
    {
        get { return (double)GetValue(BackgroundWidthProperty); }
        set { SetValue(BackgroundWidthProperty, value); }
    }

    public static readonly DependencyProperty NotificationProgressBarValueProperty = DependencyProperty.Register(
        nameof(NotificationProgressBarValue), typeof(double), typeof(MainWindow), new PropertyMetadata(default(double)));

    public double NotificationProgressBarValue
    {
        get { return (double)GetValue(NotificationProgressBarValueProperty); }
        set { SetValue(NotificationProgressBarValueProperty, value); }
    }

    public MainWindow(SettingsService settingsService, 
        IProfileService profileService,
        INotificationHostService notificationHostService, 
        ITaskBarIconService taskBarIconService,
        IThemeService themeService, 
        ILogger<MainWindow> logger, 
        ISpeechService speechService,
        IExactTimeService exactTimeService,
        TopmostEffectWindow topmostEffectWindow,
        IComponentsService componentsService,
        ILessonsService lessonsService,
        IUriNavigationService uriNavigationService,
        IRulesetService rulesetService,
        IWindowRuleService windowRuleService,
        IManagementService managementService,
        MainViewModel viewModel)
    {
        Logger = logger;
        SpeechService = speechService;
        SettingsService = settingsService;
        TaskBarIconService = taskBarIconService;
        NotificationHostService = notificationHostService;
        ThemeService = themeService;
        ProfileService = profileService;
        ExactTimeService = exactTimeService;
        TopmostEffectWindow = topmostEffectWindow;
        ComponentsService = componentsService;
        LessonsService = lessonsService;
        UriNavigationService = uriNavigationService;
        RulesetService = rulesetService;
        WindowRuleService = windowRuleService;
        ManagementService = managementService;

        IAppHost.GetService<ISplashService>().SetDetailedStatus("正在初始化主界面（步骤 1/2）");
        SettingsService.PropertyChanged += (sender, args) =>
        {
            LoadSettings();
        };
        DataContext = this;
        LessonsService.PreMainTimerTicked += LessonsServiceOnPreMainTimerTicked;
        ViewModel = viewModel;
        ViewModel.PropertyChanged += ViewModelOnPropertyChanged;
        TopmostRecheckTimer.Tick += TopmostRecheckTimerOnTick;
        InitializeComponent();
        App.GetService<NotificationDisplayService>().VisualHost = this;
        RulesetService.StatusUpdated += RulesetServiceOnStatusUpdated;
        TouchInFadingTimer.Tick += TouchInFadingTimerOnTick;
        IsRunningCompatibleMode = SettingsService.Settings.IsCompatibleWindowTransparentEnabled;
        if (IsRunningCompatibleMode)
        {
            AllowsTransparency = true;
        }
        else
        {
            SetValue(WindowChrome.WindowChromeProperty, new WindowChrome()
            {
                GlassFrameThickness = new Thickness(-1),
                CaptionHeight = 0,
                ResizeBorderThickness = new Thickness(0)
            });
        }
    }

    private void TouchInFadingTimerOnTick(object? sender, EventArgs e)
    {
        ViewModel.IsMouseIn = false;
        TouchInFadingTimer.Stop();
    }

    private void TopmostRecheckTimerOnTick(object? sender, EventArgs e)
    {
        ReCheckTopmostState();
        SetBottom();
    }

    private void UpdateTopmostRecheckTimer()
    {
        TopmostRecheckTimer.Stop();
        var interval = ViewModel.Settings.WindowTopmostRecheckMode switch
        {
            2 => 1000d,
            3 => 500d,
            4 => 200d,
            5 => 100d,
            6 => 50d,
            7 => 1d,
            8 => Math.Max(1d, ViewModel.Settings.WindowTopmostRecheckIntervalMs),
            _ => 0d
        };
        if (interval <= 0)
        {
            return;
        }

        TopmostRecheckTimer.Interval = TimeSpan.FromMilliseconds(interval);
        TopmostRecheckTimer.Start();
    }

    private void RulesetServiceOnStatusUpdated(object? sender, EventArgs e)
    {
        if (ViewModel.Settings.HideMode == 1)
        {
            ViewModel.IsHideRuleSatisfied = RulesetService.IsRulesetSatisfied(ViewModel.Settings.HiedRules);
        }
        // Detect fullscreen
        var screen = ViewModel.Settings.WindowDockingMonitorIndex < Screen.AllScreens.Length &&
                     ViewModel.Settings.WindowDockingMonitorIndex >= 0 ?
            Screen.AllScreens[ViewModel.Settings.WindowDockingMonitorIndex] : Screen.PrimaryScreen;
        if (screen != null)
        {
            ViewModel.IsForegroundFullscreen = NativeWindowHelper.IsForegroundFullScreen(screen);
            ViewModel.IsForegroundMaxWindow = NativeWindowHelper.IsForegroundMaxWindow(screen);
        }
    }

    private void LessonsServiceOnPreMainTimerTicked(object? sender, EventArgs e)
    {
        //SettingsService.Settings.IsNetworkConnect = InternetGetConnectedState(out var _);
        //SettingsService.Settings.IsNotificationSpeechEnabled = SettingsService.Settings.IsNetworkConnect || SettingsService.Settings.IsSystemSpeechSystemExist;
        if (SettingsService.Settings.IsMainWindowDebugEnabled)
            ViewModel.DebugCurrentTime = ExactTimeService.GetCurrentLocalDateTime();

        UpdateWindowPos(true);
        if (ViewModel.Settings.WindowLayer == 0)
        {
            //SetBottom();
        }
        //NotificationHostService.OnUpdateTimerTick(this, EventArgs.Empty);

        if (SettingsService.Settings.DebugTimeSpeed != 0)
        {
            SettingsService.Settings.DebugTimeOffsetSeconds += (SettingsService.Settings.DebugTimeSpeed - 1) * 0.05;
        }
    }

    private Storyboard BeginStoryboard(string name)
    {
        var a = (Storyboard)FindResource(name);
        a.Begin();
        return a;
    }

    private void UpdateMouseStatus()
    {
        if (PresentationSource.FromVisual(this) == null)
        {
            return;
        }

        try
        {
            GetCursorPos(out var ptr);
            MousePosChanged?.Invoke(this, new MousePosChangedEventArgs(ptr));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "无法更新鼠标状态。");
        }
    }

    [Obsolete]
    private bool GetMouseStatusByPos(System.Drawing.Point ptr)
    {
        return false;
    }

    public Point GetCenter()
    {
        GetCurrentDpi(out var dpi, out _);
        
        if (VisualTreeUtils.FindChildVisualByName<Grid>(this, "PART_GridWrapper") is not { } gridWrapper) 
            return _centerPointCache;  // 在切换组件配置时可能出现找不到 GridWrapper 的情况，此时要使用上一次的数值
        var p = gridWrapper.TranslatePoint(new Point(gridWrapper.ActualWidth / 2, gridWrapper.ActualHeight / 2), this);
        p.Y = Top + (ActualHeight / 2);
        return _centerPointCache = p;
    }

    public void OnNotificationTopmostChanged()
    {
        if (ViewModel.IsNotificationWindowExplicitShowed && ViewModel.Settings.WindowLayer == 0)
        {
            UpdateWindowLayer();
            ReCheckTopmostState();
        }
        else if (!ViewModel.IsNotificationWindowExplicitShowed)
        {
            SetBottom();
            UpdateWindowLayer();
        }
    }

    public void OnNotificationEffectRequested()
    {
        if (GridRoot.IsVisible && ViewModel.Settings.IsMainWindowVisible && !IsRunningCompatibleMode)
        {
            var center = GetCenter();
            TopmostEffectWindow.Dispatcher.Invoke(() =>
            {
                TopmostEffectWindow.PlayEffect(new RippleEffect()
                {
                    CenterX = center.X,
                    CenterY = center.Y
                });
            });
        }
    }

    public void OnNotificationProgressStarted(TimeSpan duration)
    {
        var da = new DoubleAnimation()
        {
            From = 1.0,
            To = 0.0,
            Duration = new Duration(duration),
        };
        var storyboard = new Storyboard()
        {
        };
        Storyboard.SetTarget(da, this);
        Storyboard.SetTargetProperty(da, new PropertyPath(NotificationProgressBarValueProperty));
        storyboard.Children.Add(da);
        NotificationProgressBar = storyboard;
        storyboard.Begin();
    }

    public void OnNotificationProgressStopped()
    {
        NotificationProgressBar.Stop();
        ViewModel.OverlayRemainStopwatch.Stop();
    }

    protected override void OnContentRendered(EventArgs e)
    {
        if (DesignerProperties.GetIsInDesignMode(this))
            return;
        IAppHost.GetService<ISplashService>().SetDetailedStatus("正在加载界面主题（2）");
        UpdateTheme();
        ViewModel.OverlayRemainTimePercents = 0.5;
        WindowRuleService.ForegroundWindowChanged += WindowRuleServiceOnForegroundWindowChanged;

        if (!ViewModel.Settings.IsNotificationEffectRenderingScaleAutoSet)
        {
            AutoSetNotificationEffectRenderingScale();
        }

        IAppHost.GetService<ISplashService>().SetDetailedStatus("正在初始化输入");
        if (SettingsService.Settings.UseRawInput)
        {
            try
            {
                InitializeRawInputHandler();
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "无法初始化 RawInput，已回退到兼容模式。");
                LessonsService.PreMainTimerTicked += ProcessMousePos;
                ViewModel.Settings.IsErrorLoadingRawInput = true;
            }
        }
        else
        {
            LessonsService.PreMainTimerTicked += ProcessMousePos;
        }

        StartupCompleted?.Invoke(this, EventArgs.Empty);

        base.OnContentRendered(e);
#if DEBUG
        MemoryProfiler.GetSnapshot("MainWindow OnContentRendered");
#endif
    }

    private void WindowRuleServiceOnForegroundWindowChanged(HWINEVENTHOOK hwineventhook, uint @event, HWND hwnd, int idobject, int idchild, uint ideventthread, uint dwmseventtime)
    {
        if (ViewModel.Settings.WindowTopmostRecheckMode == 1)
        {
            ReCheckTopmostState();
            SetBottom();
        }
    }

    private void ReCheckTopmostState()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (ViewModel.IsNotificationWindowExplicitShowed || ViewModel.Settings.WindowLayer == 1)
        {
            SetWindowPos((HWND)handle, NativeWindowHelper.HWND_TOPMOST, 0, 0, 0, 0,
                SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_NOSENDCHANGING);
            //Topmost = true;
        }
    }

    private void InitializeRawInputHandler()
    {
        var handle = new WindowInteropHelper(this).Handle;
        RawInputDevice.RegisterDevice(HidUsageAndPage.Mouse,
            RawInputDeviceFlags.InputSink, handle);
        RawInputDevice.RegisterDevice(HidUsageAndPage.TouchScreen,
            RawInputDeviceFlags.InputSink, handle);

        RawInputUpdateStopWatch.Start();
        var hWndSource = HwndSource.FromHwnd(handle);
        hWndSource?.AddHook(ProcWnd);
    }

    private void ProcessMousePos(object? sender, EventArgs e)
    {
        UpdateMouseStatus();
    }

    private IntPtr ProcWnd(IntPtr hwnd, int msg, IntPtr param, IntPtr lParam, ref bool handled)
    {
        if (msg == 0x00FF) // WM_INPUT
        {
            if (RawInputUpdateStopWatch.ElapsedMilliseconds < 20)
            {
                return IntPtr.Zero;
            }
            RawInputUpdateStopWatch.Restart();
            // Create an RawInputData from the handle stored in lParam.
            var data = RawInputData.FromHandle(lParam);
            RawInputEvent?.Invoke(this, new RawInputEventArgs(data));
        }

        if (msg == 0x0047 && ViewModel.Settings.WindowTopmostRecheckMode == 0) // WM_WINDOWPOSCHANGED
        {
            var pos = Marshal.PtrToStructure<WINDOWPOS>(lParam);
            Logger.LogTrace("WM_WINDOWPOSCHANGED {}", pos.flags);
            if ((pos.flags & SET_WINDOW_POS_FLAGS.SWP_NOZORDER) == 0) // SWP_NOZORDER
            {
                Logger.LogTrace("Z order changed");
                if (pos.hwndInsertAfter != NativeWindowHelper.HWND_TOPMOST)
                {
                    ReCheckTopmostState();
                }

                if (pos.hwndInsertAfter != NativeWindowHelper.HWND_BOTTOM)
                {
                    SetBottom();
                }
            }
        }

        return IntPtr.Zero;
    }

    private void AutoSetNotificationEffectRenderingScale()
    {
        var screen = ViewModel.Settings.WindowDockingMonitorIndex < Screen.AllScreens.Length
            ? Screen.AllScreens[ViewModel.Settings.WindowDockingMonitorIndex]
            : Screen.PrimaryScreen;
        if (screen == null)
            return;
        if (screen.Bounds.Height >= 1400)
        {
            ViewModel.Settings.NotificationEffectRenderingScale = 0.75;
        }
        if (screen.Bounds.Height >= 2000)
        {
            ViewModel.Settings.NotificationEffectRenderingScale = 0.5;
        }

        ViewModel.Settings.IsNotificationEffectRenderingScaleAutoSet = true;
    }

    public void LoadProfile()
    {
        //ProfileService.LoadProfile();
        ViewModel.Profile = ProfileService.Profile;
    }

    public void SaveProfile()
    {
        ProfileService.SaveProfile(ViewModel.CurrentProfilePath);
    }

    private void LoadSettings()
    {
        var r = SettingsService.Settings;
        ViewModel.Settings = r;
        ViewModel.Settings.PropertyChanged -= SettingsOnPropertyChanged;
        ViewModel.Settings.PropertyChanged += SettingsOnPropertyChanged;
    }

    public void SettingsOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        UpdateTheme();
        if (e.PropertyName is nameof(ViewModel.Settings.IsMouseInFadingReversed)
                           or nameof(ViewModel.Settings.IsMouseInFadingEnabled))
        {
            UpdateFadeStatus();
        }
        if (e.PropertyName == nameof(ViewModel.Settings.UseSelfDrawnIsland))
        {
            UpdateIslandRenderMode();
        }
    }

    /// <summary>根据设置决定是否隐藏 XAML 主界面（切换为 IslandHost 自绘）。</summary>
    private void UpdateIslandRenderMode()
    {
        ResourceLoaderBorder.Visibility = ViewModel.Settings.UseSelfDrawnIsland
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void ViewModelOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModel.IsMouseIn))
        {
            UpdateFadeStatus();
        }
    }

    private void UpdateFadeStatus()
    {
        ViewModel.IsMainWindowFaded =
            ViewModel.Settings.IsMouseInFadingEnabled &&
           (ViewModel.IsMouseIn ^ ViewModel.Settings.IsMouseInFadingReversed);
    }

    protected override void OnInitialized(EventArgs e)
    {
        base.OnInitialized(e);
        if (DesignerProperties.GetIsInDesignMode(this))
            return;
        ViewModel.Profile.PropertyChanged += (sender, args) => SaveProfile();
        LoadSettings();
        UpdateIslandRenderMode();
        //ViewModel.CurrentProfilePath = ViewModel.Settings.SelectedProfile;
        LoadProfile();
        IAppHost.GetService<ISplashService>().SetDetailedStatus("正在加载界面主题（1）");
        UpdateTheme();
        ThemeService.ThemeUpdated += (_, _) => UpdateTheme();
    }

    private void SetBottom()
    {
        var hWnd = (HWND)new WindowInteropHelper(this).Handle;
        if (ViewModel.Settings.WindowLayer != 0)
        {
            return;
        }
        if (ViewModel.IsNotificationWindowExplicitShowed)
        {
            //SetWindowPos(hWnd, NativeWindowHelper.HWND_TOPMOST, 0, 0, 0, 0,
            //    SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
            return;
        }
        SetWindowPos(hWnd, NativeWindowHelper.HWND_BOTTOM, 0, 0, 0, 0,
            SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
    }

    private void UpdateTheme()
    {
        UpdateWindowPos();
        UpdateTopmostRecheckTimer();
        var hWnd = (HWND)new WindowInteropHelper(this).Handle;
        var style = GetWindowLong(hWnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        if (ViewModel.Settings.IsScreenRecordingModeEnabled)
        {
            style &= ~NativeWindowHelper.WS_EX_TOOLWINDOW;
        }
        else
        {
            style |= NativeWindowHelper.WS_EX_TOOLWINDOW;
        }

        if (ViewModel.Settings.IsMouseClickingEnabled)
        {
            style &= ~NativeWindowHelper.WS_EX_TRANSPARENT;
        }
        else
        {
            style |= NativeWindowHelper.WS_EX_TRANSPARENT;
        }

        SetWindowLong(hWnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, style);

        SetWindowDisplayAffinity(hWnd, ViewModel.Settings.IsWindowCaptureBlockingEnabled
            ? WINDOW_DISPLAY_AFFINITY.WDA_EXCLUDEFROMCAPTURE
            : WINDOW_DISPLAY_AFFINITY.WDA_NONE);

        UpdateWindowLayer();

        ResourceLoaderBorder.Resources[nameof(SettingsService.Settings.MainWindowSecondaryFontSize)] =
            SettingsService.Settings.MainWindowSecondaryFontSize;
        ResourceLoaderBorder.Resources[nameof(SettingsService.Settings.MainWindowBodyFontSize)] =
            SettingsService.Settings.MainWindowBodyFontSize;
        ResourceLoaderBorder.Resources[nameof(SettingsService.Settings.MainWindowEmphasizedFontSize)] =
            SettingsService.Settings.MainWindowEmphasizedFontSize;
        ResourceLoaderBorder.Resources[nameof(SettingsService.Settings.MainWindowLargeFontSize)] =
            SettingsService.Settings.MainWindowLargeFontSize;

        if (ViewModel.Settings.IsCustomForegroundColorEnabled)
        {
            var brush = new SolidColorBrush(ViewModel.Settings.CustomForegroundColor);
            ResourceLoaderBorder.SetValue(ForegroundProperty, brush);
            ResourceLoaderBorder.SetValue(TextElement.ForegroundProperty, brush);
            ResourceLoaderBorder.Resources["MahApps.Brushes.ThemeForeground"] = brush;
        }
        else
        {
            if (ResourceLoaderBorder.Resources.Contains("MahApps.Brushes.ThemeForeground"))
            {
                ResourceLoaderBorder.Resources.Remove("MahApps.Brushes.ThemeForeground");
            }
            ResourceLoaderBorder.SetValue(ForegroundProperty, DependencyProperty.UnsetValue);
            ResourceLoaderBorder.SetValue(TextElement.ForegroundProperty, DependencyProperty.UnsetValue);
        }
    }

    private void UpdateWindowLayer()
    {
        switch (ViewModel.Settings.WindowLayer)
        {
            case 0: // bottom
                Topmost = ViewModel.IsNotificationWindowExplicitShowed;
                break;
            case 1:
                Topmost = true;
                break;
        }
    }

    private void ListView_OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
    }

    private void Selector_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        e.Handled = true;
    }

    private void MainWindow_OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            e.Handled = true;
            //DragMove();
        }
    }

    private void MainWindow_OnClosing(object? sender, CancelEventArgs e)
    {
        if (!ViewModel.IsClosing)
        {
            e.Cancel = true;
            return;
        }
        AppBase.Current.Stop();
    }

    private void UpdateWindowPos(bool updateEffectWindow=false)
    {
        GetCurrentDpi(out var dpiX, out var dpiY);

        var scale = ViewModel.Settings.Scale;
        ViewModel.GridRootLeft = Width / 10 * (scale - 1);
        ViewModel.GridRootTop = Height / 10 * (scale - 1);

        var screen = ViewModel.Settings.WindowDockingMonitorIndex < Screen.AllScreens.Length  && ViewModel.Settings.WindowDockingMonitorIndex >= 0
            ? Screen.AllScreens[ViewModel.Settings.WindowDockingMonitorIndex] 
            : Screen.PrimaryScreen;
        if (screen == null)
            return;
        double offsetAreaTop = ViewModel.Settings.IsIgnoreWorkAreaEnabled ? screen.Bounds.Top : screen.WorkingArea.Top;
        double offsetAreaBottom = ViewModel.Settings.IsIgnoreWorkAreaEnabled ? screen.Bounds.Bottom : screen.WorkingArea.Bottom;
        var aw = RenderSize.Width * dpiX;
        var ah = RenderSize.Height * dpiY;
        var c = (double)(screen.WorkingArea.Left + screen.WorkingArea.Right) / 2;
        var ox = ViewModel.Settings.WindowDockingOffsetX;
        var oy = ViewModel.Settings.WindowDockingOffsetY;
        Width = screen.WorkingArea.Width / dpiX;
        //Height = GridRoot.ActualHeight * scale;
        Left = (screen.WorkingArea.Left + ox) / dpiX;

        switch (ViewModel.Settings.WindowDockingLocation)
        {
            case 0: //左上
                //Left = (screen.WorkingArea.Left + ox) / dpiX;
                Top = (offsetAreaTop + oy) / dpiY;
                break;
            case 1: // 中上
                //Left = (c - aw / 2 + ox) / dpiX;
                Top = (offsetAreaTop + oy) / dpiY;
                break;
            case 2: // 右上
                //Left = (screen.WorkingArea.Right - aw + ox) / dpiX;
                Top = (offsetAreaTop + oy) / dpiY;
                break;
            case 3: // 左下
                //Left = (screen.WorkingArea.Left + ox) / dpiX;
                Top = (offsetAreaBottom - ah + oy) / dpiY;
                break;
            case 4: // 中下
                //Left = (c - aw / 2 + ox) / dpiX;
                Top = (offsetAreaBottom - ah + oy) / dpiY;
                break;
            case 5: // 右下
                //Left = (screen.WorkingArea.Right - aw + ox) / dpiX;
                Top = (offsetAreaBottom - ah + oy) / dpiY;
                break;
        }

        if (updateEffectWindow)
        {
            TopmostEffectWindow.Dispatcher.Invoke(() =>
            {
                TopmostEffectWindow.UpdateWindowPos(screen, 1 / dpiX);
            });
        }
    }

    public void GetCurrentDpi(out double dpiX, out double dpiY, Visual? visual=null)
    {
        dpiX = _latestDpiX;
        dpiY = _latestDpiY;
        var realVisual = visual ?? this;
        try
        {
            var source = PresentationSource.FromVisual(realVisual);
            if (source?.CompositionTarget == null) 
                return;
            _latestDpiX = dpiX = 1.0 * source.CompositionTarget.TransformToDevice.M11;
            _latestDpiY = dpiY = 1.0 * source.CompositionTarget.TransformToDevice.M22;
        }
        catch(Exception ex)
        {
            Logger.LogError(ex, "无法获取当前dpi");
        }
    }

    private void MainWindow_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        //UpdateWindowPos();
    }

    private void MainWindow_OnActivated(object? sender, EventArgs e)
    {
        SetBottom();
    }

    private void MainWindow_OnStateChanged(object? sender, EventArgs e)
    {
        SetBottom();
    }

    public void OpenProfileSettingsWindow()
    {
        App.GetService<ProfileSettingsWindow>().Open();
    }

    private void GridRoot_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        Width = e.NewSize.Width * ViewModel.Settings.Scale;
        Height = e.NewSize.Height * ViewModel.Settings.Scale;
    }

    private void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        ((HwndSource)PresentationSource.FromVisual(this)).AddHook((IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
        {
            //想要让窗口透明穿透鼠标和触摸等，需要同时设置 WS_EX_LAYERED 和 WS_EX_TRANSPARENT 样式，
            //确保窗口始终有 WS_EX_LAYERED 这个样式，并在开启穿透时设置 WS_EX_TRANSPARENT 样式
            //但是WPF窗口在未设置 AllowsTransparency = true 时，会自动去掉 WS_EX_LAYERED 样式（在 HwndTarget 类中)，
            //如果设置了 AllowsTransparency = true 将使用WPF内置的低性能的透明实现，
            //所以这里通过 Hook 的方式，在不使用WPF内置的透明实现的情况下，强行保证这个样式存在。
            if (msg == (int)0x007C && (long)wParam == (long)WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE)
            {
                var styleStruct = (NativeWindowHelper.StyleStruct)Marshal.PtrToStructure(lParam, typeof(NativeWindowHelper.StyleStruct));
                styleStruct.styleNew |= (int)NativeWindowHelper.WS_EX_LAYERED;
                Marshal.StructureToPtr(styleStruct, lParam, false);
                handled = true;
            }
            return IntPtr.Zero;
        });
    }

    private void GridWrapper_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (double.IsNaN(e.NewSize.Width))
            return;
        if ( double.IsNaN(BackgroundWidth))
        {
            BackgroundWidth = e.NewSize.Width;
            return;
        }

        var m = e.NewSize.Width > BackgroundWidth;
        var s = ViewModel.Settings.DebugAnimationScale;
        var t = m ? 600 * s : 800 * s;
        var da = new DoubleAnimation()  
        {
            From = BackgroundWidth,
            To = e.NewSize.Width,
            Duration = new Duration(TimeSpan.FromMilliseconds(t)),
            EasingFunction = m ? new BackEase()
            {
                EasingMode = EasingMode.EaseOut,
                Amplitude = 0.4
            } : new BackEase()
            {
                EasingMode = EasingMode.EaseOut,
                Amplitude = 0.2
            }
        };
        var storyboard = new Storyboard()
        {
        };
        Storyboard.SetTarget(da, this);
        Storyboard.SetTargetProperty(da, new PropertyPath(BackgroundWidthProperty));
        storyboard.Children.Add(da);
        storyboard.Begin();
        storyboard.Completed += (o, args) =>
        {
            storyboard.Remove();
        };
    }

    private void TrayIconOnClicked_OnExecuted(object sender, ExecutedRoutedEventArgs e)
    {
    }
}
