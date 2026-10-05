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
        FreezeApplicationResources();
    }

    /// <summary>
    /// Splash is created on the AsyncBox dispatcher. Shared brushes must be frozen
    /// on the main thread first, or that dispatcher seals them and MainWindow cannot use them.
    /// </summary>
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