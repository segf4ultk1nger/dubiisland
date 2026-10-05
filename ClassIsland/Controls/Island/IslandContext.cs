using System.Windows.Media;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Models;

namespace ClassIsland.Controls.Island;

/// <summary>
/// 岛渲染上下文：向 <see cref="IIslandComponent"/> 暴露设置、字体与课表服务，不依赖任何 XAML。
/// </summary>
public sealed class IslandContext
{
    private FontFamily? _fontFamily;

    public Settings Settings { get; }

    public ILessonsService LessonsService { get; }

    public IProfileService ProfileService { get; }

    public IExactTimeService ExactTimeService { get; }

    public IRulesetService RulesetService { get; }

    public IWeatherService WeatherService { get; }

    /// <summary>当前 DPI 缩放，FormattedText 需用它做像素对齐。</summary>
    public double PixelsPerDip { get; set; } = 1.0;

    /// <summary>主题主色，供进度条、倒计时等使用。</summary>
    public Color AccentColor { get; set; } = Color.FromRgb(0x1E, 0x90, 0xFF);

    /// <summary>前景色，供文字使用。</summary>
    public Color ForegroundColor { get; set; } = Colors.White;

    /// <summary>主题背景色，非自定义背景时使用。</summary>
    public Color ThemeBackground { get; set; } = Color.FromRgb(0x1F, 0x1F, 0x1F);

    public IslandContext(Settings settings, ILessonsService lessonsService,
        IProfileService profileService, IExactTimeService exactTimeService,
        IRulesetService rulesetService, IWeatherService weatherService)
    {
        Settings = settings;
        LessonsService = lessonsService;
        ProfileService = profileService;
        ExactTimeService = exactTimeService;
        RulesetService = rulesetService;
        WeatherService = weatherService;
    }

    public FontFamily FontFamily => _fontFamily ??= new FontFamily(Settings.MainWindowFont);

    /// <summary>字体设置变化后清除缓存的 <see cref="FontFamily"/>。</summary>
    public void InvalidateFont() => _fontFamily = null;

    public double SecondaryFontSize => Settings.MainWindowSecondaryFontSize;

    public double BodyFontSize => Settings.MainWindowBodyFontSize;

    public double EmphasizedFontSize => Settings.MainWindowEmphasizedFontSize;

    public double LargeFontSize => Settings.MainWindowLargeFontSize;
}
