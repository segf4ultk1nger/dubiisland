using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Shell;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Abstractions.Views;
using ClassIsland.Core.Helpers.Native;
using ClassIsland.Services;

namespace ClassIsland.Views;

/// <summary>
/// SplashWindow.xaml 的交互逻辑
/// </summary>
public partial class SplashWindow : SplashWindowBase
{
    public ISplashService SplashService { get; }

    public SplashWindow(ISplashService splashService, SettingsService settingsService)
    {
        SplashService = splashService;
        InitializeComponent();
        // 默认非透明分层窗口 + DWM 玻璃框（GPU 合成路径），与 MainWindow / 自绘岛一致；
        // 兼容模式下退回 AllowsTransparency。必须在创建 HWND 前决定。
        if (settingsService.Settings.IsCompatibleWindowTransparentEnabled)
        {
            AllowsTransparency = true;
        }
        else
        {
            SetValue(WindowChrome.WindowChromeProperty, new WindowChrome
            {
                GlassFrameThickness = new Thickness(-1),
                CaptionHeight = 0,
                ResizeBorderThickness = new Thickness(0)
            });
        }
    }

    public bool IsRendered => _isInit1 && _isInit2;

    private bool _isInit1 = false;
    private bool _isInit2 = false;

    protected override void OnContentRendered(EventArgs e)
    {
        // 标记为工具窗口，避免出现在 Alt+Tab 中。
        var hWnd = (HWND)new WindowInteropHelper(this).Handle;
        var style = GetWindowLong(hWnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        SetWindowLong(hWnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, style | NativeWindowHelper.WS_EX_TOOLWINDOW);
        base.OnContentRendered(e);
        _isInit1 = true;
    }

    private void SplashWindow_OnClosed(object? sender, EventArgs e)
    {
    }

    private void FrameworkElement_OnLoaded(object sender, RoutedEventArgs e)
    {
        _isInit2 = true;
    }
}