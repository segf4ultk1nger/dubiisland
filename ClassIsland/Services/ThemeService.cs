using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Models.Theming;
using ControlzExThemeManager = ControlzEx.Theming.ThemeManager;
using ControlzExRuntimeThemeGenerator = ControlzEx.Theming.RuntimeThemeGenerator;

using MahApps.Metro.Theming;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace ClassIsland.Services;

public class ThemeService : IHostedService, IThemeService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
    }

    public Color PrimaryColor { get; set; }

    private Color? _appliedPrimary;
    private Color? _appliedSecondary;
    private int _appliedRealThemeMode = -1; 

    public ILogger<ThemeService> Logger { get; }

    public event EventHandler<ThemeUpdatedEventArgs>? ThemeUpdated;

    public ThemeService(ILogger<ThemeService> logger)
    {
        Logger = logger;
    }

    public int CurrentRealThemeMode { get; set; } = 0;

    public void SetTheme(int themeMode, Color primary, Color secondary)
    {
        var useLight = ResolveUseLight(themeMode);
        var realMode = useLight ? 0 : 1;
        var mahAppsBaseColor = useLight
            ? ControlzExThemeManager.BaseColorLight
            : ControlzExThemeManager.BaseColorDark;
        if (_appliedPrimary == primary &&
            _appliedSecondary == secondary &&
            _appliedRealThemeMode == realMode &&
            MahAppsThemeMatches(mahAppsBaseColor, primary))
        {
            return;
        }

        ApplyMahAppsTheme(mahAppsBaseColor, primary);
        PrimaryColor = primary;
        _appliedPrimary = primary;
        _appliedSecondary = secondary;
        _appliedRealThemeMode = realMode;
        Logger.LogInformation("设置主题：{Color} {Mode}", primary, useLight ? "Light" : "Dark");
        CurrentRealThemeMode = realMode;
        ThemeUpdated?.Invoke(this, new ThemeUpdatedEventArgs
        {
            ThemeMode = themeMode,
            Primary = primary,
            Secondary = secondary,
            RealThemeMode = realMode
        });

        var resource = new ResourceDictionary
        {
            Source = realMode == 0 ?
                new Uri("pack://application:,,,/ClassIsland;component/Themes/LightTheme.xaml") :
                new Uri("pack://application:,,,/ClassIsland;component/Themes/DarkTheme.xaml")
        };
        Application.Current.Resources.MergedDictionaries[0] = resource;
        // 默认不冻结应用资源：见 FreezeApplicationResources 的说明（省约 0.3s 启动）。跨线程共享资源出问题时再启用。
        //FreezeApplicationResources();
    }

    /// <summary>
    /// Splash is created on the AsyncBox dispatcher. Shared brushes must be frozen
    /// on the main thread first, or that dispatcher seals them and MainWindow cannot use them.
    /// </summary>
    /// <remarks>
    /// 目前**未启用**（两处调用点均已注释）。本方法会遍历并强制物化整个应用资源树后再逐个 Freeze，
    /// 实测占用启动约 0.4–0.5s，其中几乎全部是物化延迟资源（Style/ControlTemplate）的开销；只冻结 Brush
    /// 同样无改善（实测 466ms）。禁用后启动省下约 0.3s，闪屏、设置窗口、主题切换、通知特效独立 UI 线程
    /// 均正常。若后续在第二 UI 线程（闪屏 / <c>NotificationUseStandaloneEffectUiThread</c> 的特效窗口）
    /// 与主线程之间出现共享资源跨线程异常，再启用调用点；届时更优做法是**只按 key 精确冻结真正共享的画笔**，
    /// 而不是遍历整棵资源树。
    /// </remarks>
    public static void FreezeApplicationResources()
    {
        if (Application.Current == null)
        {
            return;
        }

        FreezeDictionary(Application.Current.Resources);
    }

    private static void FreezeDictionary(ResourceDictionary dictionary)
    {
        foreach (var merged in dictionary.MergedDictionaries)
        {
            FreezeDictionary(merged);
        }

        foreach (var key in dictionary.Keys)
        {
            try
            {
                if (dictionary[key] is Freezable freezable && freezable.CanFreeze && !freezable.IsFrozen)
                {
                    freezable.Freeze();
                }
            }
            catch (Exception)
            {
                // Deferred theme resources can fail to materialize, or a brush can refuse to freeze.
            }
        }
    }

    private bool ResolveUseLight(int themeMode)
    {
        switch (themeMode)
        {
            case 1:
                return true;
            case 2:
                return false;
            default:
                return DetectSystemUseLight();
        }
    }

    private static bool DetectSystemUseLight()
    {
        try
        {
            var key = Registry.CurrentUser.OpenSubKey(
                "Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize");
            if (key != null && (int?)key.GetValue("AppsUseLightTheme") == 0)
            {
                return false;
            }
        }
        catch (Exception)
        {
            // ignored
        }

        return true;
    }

    /// <summary>
    /// 在根据设置应用主题之前，先套用一套默认（跟随系统明暗模式）的 MahApps 主题。
    /// 否则在主题应用前创建的窗口（启动检查对话框、单实例提示、闪屏等）会因缺少
    /// MahApps.Brushes.* 主题资源而渲染成全黑。
    /// </summary>
    public static void ApplyStartupTheme()
    {
        if (Application.Current == null)
        {
            return;
        }

        ApplyMahAppsTheme(
            DetectSystemUseLight() ? ControlzExThemeManager.BaseColorLight : ControlzExThemeManager.BaseColorDark,
            Colors.DodgerBlue);
    }

    private static bool MahAppsThemeMatches(string baseColorScheme, Color primary)
    {
        if (Application.Current == null)
        {
            return false;
        }

        var detected = ControlzExThemeManager.Current.DetectTheme(Application.Current);
        return detected != null
               && detected.BaseColorScheme == baseColorScheme
               && detected.PrimaryAccentColor == primary;
    }

    private static void ApplyMahAppsTheme(string baseColorScheme, Color primary)
    {
        // Registers the MahApps library theme provider. GenerateRuntimeTheme(base, accent)
        // has no secondary-color parameter, so the primary color is the accent.
        _ = MahAppsLibraryThemeProvider.DefaultInstance;
        var runtimeTheme = ControlzExRuntimeThemeGenerator.Current.GenerateRuntimeTheme(baseColorScheme, primary);
        if (runtimeTheme == null || Application.Current == null)
        {
            return;
        }

        ControlzExThemeManager.Current.ChangeTheme(Application.Current, runtimeTheme);
    }

}