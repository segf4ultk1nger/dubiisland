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
using MaterialDesignThemes.Wpf;

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

    public ITheme? CurrentTheme { get; set; } 

    public ILogger<ThemeService> Logger { get; }

    public event EventHandler<ThemeUpdatedEventArgs>? ThemeUpdated;

    public ThemeService(ILogger<ThemeService> logger)
    {
        Logger = logger;
    }

    public int CurrentRealThemeMode { get; set; } = 0;

    public void SetTheme(int themeMode, Color primary, Color secondary)
    {
        var paletteHelper = new PaletteHelper();
        var theme = paletteHelper.GetTheme();
        var lastPrimary = theme.PrimaryMid.Color;
        var lastSecondary = theme.SecondaryMid.Color;
        var lastBaseTheme = theme.GetBaseTheme();
        switch (themeMode)
        {
            case 0:
                try
                {
                    var key = Registry.CurrentUser.OpenSubKey(
                        "Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize");
                    if (key != null)
                    {
                        if ((int?)key.GetValue("AppsUseLightTheme") == 0)
                        {
                            theme.SetBaseTheme(new MaterialDesignDarkTheme());
                        }
                        else
                        {
                            theme.SetBaseTheme(new MaterialDesignLightTheme());
                        }
                    }
                }
                catch(Exception ex)
                {
                    Logger.LogError(ex, "无法获取系统明暗主题，使用默认（亮色）主题。");
                    theme.SetBaseTheme(new MaterialDesignLightTheme());
                }
                break;

            case 1:
                theme.SetBaseTheme(new MaterialDesignLightTheme());
                break;
            case 2:
                theme.SetBaseTheme(new MaterialDesignDarkTheme());
                break;
        }
        
        ((Theme)theme).ColorAdjustment = new ColorAdjustment()
        {
            DesiredContrastRatio = 4.5F,
            Contrast = Contrast.Medium,
            Colors = ColorSelection.All
        };
        
        theme.SetPrimaryColor(primary);
        theme.SetSecondaryColor(secondary);
        var lastTheme = paletteHelper.GetTheme();

        var mahAppsBaseColor = theme.GetBaseTheme() == BaseTheme.Light
            ? ControlzExThemeManager.BaseColorLight
            : ControlzExThemeManager.BaseColorDark;
        if (lastPrimary == theme.PrimaryMid.Color &&
            lastSecondary == theme.SecondaryMid.Color &&
            lastBaseTheme == theme.GetBaseTheme() &&
            MahAppsThemeMatches(mahAppsBaseColor, primary))
        {
            return;
        }


        paletteHelper.SetTheme(theme);
        ApplyMahAppsTheme(mahAppsBaseColor, primary);
        CurrentTheme = theme;
        Logger.LogInformation("设置主题：{}", theme);
        CurrentRealThemeMode = theme.GetBaseTheme() == BaseTheme.Light ? 0 : 1;
        ThemeUpdated?.Invoke(this, new ThemeUpdatedEventArgs
        {
            ThemeMode = themeMode,
            Primary = primary,
            Secondary = secondary,
            RealThemeMode = theme.GetBaseTheme() == BaseTheme.Light ? 0 : 1
        });

        var resource = new ResourceDictionary
        {
            Source = CurrentRealThemeMode == 0 ?
                new Uri("pack://application:,,,/ClassIsland;component/Themes/LightTheme.xaml") :
                new Uri("pack://application:,,,/ClassIsland;component/Themes/DarkTheme.xaml")
        };
        Application.Current.Resources.MergedDictionaries[0] = resource;
        EnsureMahAppsMaterialAliasesLast();
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