using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using ClassIsland.Shared;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Models.Theming;
using MahApps.Metro.Controls;
using Adorner = System.Windows.Forms.Design.Behavior.Adorner;

namespace ClassIsland.Core.Controls;

/// <summary>
/// 通用窗口基类
/// </summary>
public class MyWindow : MetroWindow
{
    private bool _isAdornerAdded;

    /// <summary>
    /// 是否显示开源警告水印
    /// </summary>
    public static bool ShowOssWatermark { get; internal set; } = false;

    private IThemeService? ThemeService { get; }

    /// <summary>
    /// 构造函数
    /// </summary>
    public MyWindow()
    {
        try
        {
            ThemeService = IAppHost.GetService<IThemeService>();
            ThemeService.ThemeUpdated += ThemeServiceOnThemeUpdated;
            IAppHost.GetService<IHangService>().AssumeHang();
        }
        catch
        {
            // ignored
        }
        Loaded += OnLoaded;
        Initialized += OnInitialized;
    }

    private const string VisualStudioControlsUriString =
        "pack://application:,,,/MahApps.Metro;component/Styles/VS/Controls.xaml";

    /// <summary>
    /// 除课表窗口（MainWindow，普通 Window）外的窗口套用 MahApps 的 Visual Studio 控件模板。
    /// 不合并 VS/Colors，颜色继续跟随应用主题。
    /// </summary>
    private void OnInitialized(object? sender, System.EventArgs e)
    {
        try
        {
            Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri(VisualStudioControlsUriString)
            });
        }
        catch
        {
            // ignored
        }

        // VS/Colors 未合并，VS 的菜单系列样式引用了只存在于 VS/Colors 的画刷（如 MenuItem.BackgroundHighlighted、
        // MenuSeparator.Border），会渲染成透明/未定义。菜单改用跟随主题的基础样式。
        var app = Application.Current;
        if (app?.TryFindResource("MahApps.Styles.MenuItem") is Style menuItemStyle)
        {
            Resources[typeof(MenuItem)] = new Style(typeof(MenuItem), menuItemStyle);
        }

        if (app?.TryFindResource("MahApps.Styles.Menu") is Style menuStyle)
        {
            Resources[typeof(Menu)] = new Style(typeof(Menu), menuStyle);
        }

        if (app?.TryFindResource("MahApps.Styles.ContextMenu") is Style contextMenuStyle)
        {
            Resources[typeof(ContextMenu)] = new Style(typeof(ContextMenu), contextMenuStyle);
        }

        if (app?.TryFindResource(MenuItem.SeparatorStyleKey) is Style separatorStyle)
        {
            Resources[MenuItem.SeparatorStyleKey] = new Style(typeof(Separator), separatorStyle);
        }

        try
        {
            SetResourceReference(StyleProperty, "MahApps.Styles.MetroWindow.VisualStudio");
        }
        catch
        {
            // ignored
        }

        // VS/Colors 未合并时，VS 的 ScrollBar 样式缺少刷子会变得不可见，回退到默认滚动条样式。
        if (TryFindResource("MahApps.Styles.ScrollBar") is Style scrollBarStyle)
        {
            Resources[typeof(ScrollBar)] = new Style(typeof(ScrollBar), scrollBarStyle);
        }

        // 去掉聚焦/失焦时的窗口发光边框（ControlzEx 用独立的 GlowWindow 绘制）。
        GlowColor = null;
        NonActiveGlowColor = null;
        GlowDepth = 0;
        BorderBrush = Brushes.Transparent;
        NonActiveBorderBrush = Brushes.Transparent;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateImmersiveDarkMode(ThemeService?.CurrentRealThemeMode ?? 1);

        if ((AppBase.Current.IsDevelopmentBuild || ShowOssWatermark)&& Content is UIElement element && !_isAdornerAdded)
        {
            var layer = AdornerLayer.GetAdornerLayer(element);
            layer?.Add(new DevelopmentBuildAdorner(element, AppBase.Current.IsDevelopmentBuild, ShowOssWatermark));
            _isAdornerAdded = true;
        }
    }

    private void ThemeServiceOnThemeUpdated(object? sender, ThemeUpdatedEventArgs e)
    {
        UpdateImmersiveDarkMode(e.RealThemeMode);
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        UpdateImmersiveDarkMode(ThemeService?.CurrentRealThemeMode ?? 1);
        Debug.WriteLine("rendered.");
    }

    private unsafe void UpdateImmersiveDarkMode(int mode)
    {
        var trueVal = 0x01;
        var falseVal = 0x00;
        var hWnd = (HWND)new WindowInteropHelper(this).Handle;
        var build = Environment.OSVersion.Version.Build;
        if (build < 17763)
        {
            return;
        }
        //Debug.WriteLine(build);

        if (mode == 0)
        {
            PInvoke.DwmSetWindowAttribute(hWnd,
                DWMWINDOWATTRIBUTE.DWMWA_USE_IMMERSIVE_DARK_MODE,
                &falseVal,
                (uint)Marshal.SizeOf(typeof(int)));
        }
        else
        {
            PInvoke.DwmSetWindowAttribute(hWnd,
                DWMWINDOWATTRIBUTE.DWMWA_USE_IMMERSIVE_DARK_MODE,
                &trueVal,
                (uint)Marshal.SizeOf(typeof(int)));
        }

        // 在Windows10系统上强制刷新标题栏
        if (build < 22000)
        {
            uint WM_NCACTIVATE = 0x0086;
            PInvoke.SendMessage(hWnd, WM_NCACTIVATE, new WPARAM((nuint)(!IsActive ? 1 : 0)), 0);
            PInvoke.SendMessage(hWnd, WM_NCACTIVATE, new WPARAM((nuint)(IsActive ? 1 : 0)), 0);
        }
    }
}