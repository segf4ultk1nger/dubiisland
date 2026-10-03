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
    private const string MahAppsMaterialAliasesUriString =
        "pack://application:,,,/ClassIsland.Core;component/Themes/MahAppsMaterialAliases.xaml";

    private static readonly Uri MahAppsMaterialAliasesUri = new(MahAppsMaterialAliasesUriString);
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
        EnsureMahAppsMaterialAliasesLast();
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
                try
                {
                    var key = Registry.CurrentUser.OpenSubKey(
                        "Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize");
                    if (key != null && (int?)key.GetValue("AppsUseLightTheme") == 0)
                    {
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "无法获取系统明暗主题，使用默认（亮色）主题。");
                }

                return true;
        }
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

    private static void EnsureMahAppsMaterialAliasesLast()
    {
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        for (var i = dictionaries.Count - 1; i >= 0; i--)
        {
            var source = dictionaries[i].Source;
            if (source != null &&
                string.Equals(source.OriginalString, MahAppsMaterialAliasesUriString, StringComparison.OrdinalIgnoreCase))
            {
                dictionaries.RemoveAt(i);
            }
        }

        dictionaries.Add(new ResourceDictionary
        {
            Source = MahAppsMaterialAliasesUri
        });
    }
}