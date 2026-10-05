using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Media;
using ClassIsland.Core;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Helpers.Native;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace ClassIsland.Services;

/// <summary>
/// 主题应用服务。负责计算并应用非视觉部分的主题（主题色、明暗模式），同步教学安全模式状态，
/// 并在设置变更或系统主题变更时自动重新应用。不依赖任何主界面窗口。
/// </summary>
public class ThemeApplyService
{
    private readonly SettingsService _settingsService;
    private readonly IThemeService _themeService;
    private readonly ILogger<ThemeApplyService> _logger;

    private readonly Stopwatch _userPreferenceUpdateStopwatch = new();

    public ThemeApplyService(SettingsService settingsService, IThemeService themeService,
        ILogger<ThemeApplyService> logger)
    {
        _settingsService = settingsService;
        _themeService = themeService;
        _logger = logger;

        _settingsService.Settings.PropertyChanged += OnSettingsChanged;
        _userPreferenceUpdateStopwatch.Start();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        AppBase.Current.AppStopping += OnAppStopping;

        ApplyTheme();
    }

    /// <summary>
    /// 根据当前设置计算并应用主题色与明暗模式，并同步教学安全模式状态。
    /// </summary>
    public void ApplyTheme()
    {
        var settings = _settingsService.Settings;
        var primary = Colors.DodgerBlue;
        var secondary = Colors.DodgerBlue;
        switch (settings.ColorSource)
        {
            case 0: //custom
                primary = settings.PrimaryColor;
                secondary = settings.SecondaryColor;
                break;
            case 1: // 壁纸主题色
            case 3: // 屏幕主题色
                primary = secondary = settings.SelectedPlatte;
                break;
            case 2:
                try
                {
                    DwmGetColorizationColor(out var color, out _);
                    var c = NativeWindowHelper.GetColor((int)color);
                    primary = secondary = c;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "获取系统主题色失败。");
                }
                break;
            case 4: // 品牌色
                primary = secondary = Color.FromRgb(0xFD, 0x80, 0x07);
                break;
        }

        _themeService.SetTheme(settings.Theme, primary, secondary);

        App._isCriticalSafeModeEnabled = settings.IsCriticalSafeMode;
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e) => ApplyTheme();

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (_userPreferenceUpdateStopwatch.ElapsedMilliseconds < 1000)
        {
            return;
        }

        _userPreferenceUpdateStopwatch.Restart();
        ApplyTheme();
    }

    private void OnAppStopping(object? sender, EventArgs e) =>
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
}
