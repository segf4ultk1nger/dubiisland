using System;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using ClassIsland.Core;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Helpers.Native;
using ClassIsland.Core.Models.Weather;
using ClassIsland.Shared.Models.Profile;
using ClassIsland.ViewModels;

namespace ClassIsland.Services.Scripting;

/// <summary>
/// 脚本 <c>lessons</c> 全局对象的宿主实现，属性读取实时课程状态。
/// </summary>
public sealed class LessonsFacade
{
    private readonly ILessonsService _lessons;
    private readonly IExactTimeService _exactTimeService;
    private readonly IProfileService _profileService;

    /// <summary>
    /// 初始化一个 <see cref="LessonsFacade"/> 实例。
    /// </summary>
    public LessonsFacade(ILessonsService lessons, IExactTimeService exactTimeService, IProfileService profileService)
    {
        _lessons = lessons;
        _exactTimeService = exactTimeService;
        _profileService = profileService;
    }

    private ClassPlan? ClassPlan => _lessons.CurrentClassPlan;
    private TimeLayout? Layout => ClassPlan?.TimeLayout;

    /// <summary>主计时器是否正在运行。</summary>
    public bool IsTimerRunning => _lessons.IsTimerRunning;

    /// <summary>当前时间状态。</summary>
    public string CurrentState => _lessons.CurrentState.ToString();

    /// <summary>当前科目名称。</summary>
    public string? CurrentSubject => SubjectName(_lessons.CurrentSubject);

    /// <summary>当前科目 ID。</summary>
    public string? CurrentSubjectId
    {
        get
        {
            var item = _lessons.CurrentTimeLayoutItem;
            return item.TimeType == 0 ? SubjectIdAt(item) : null;
        }
    }

    /// <summary>下一节课科目名称。</summary>
    public string? NextSubject => SubjectName(_lessons.NextClassSubject);

    /// <summary>下一节课科目 ID。</summary>
    public string? NextSubjectId => SubjectIdAt(_lessons.NextClassTimeLayoutItem);

    /// <summary>上一节课科目名称。</summary>
    public string? PreviousSubject
    {
        get
        {
            var id = PreviousSubjectId;
            return id != null && _profileService.Profile.Subjects.TryGetValue(Guid.Parse(id), out var subject)
                ? subject.Name
                : null;
        }
    }

    /// <summary>上一节课科目 ID。</summary>
    public string? PreviousSubjectId => SubjectIdAt(PreviousClassItem());

    /// <summary>当前时间点信息。</summary>
    public ScriptTimeLayoutItemInfo? CurrentTimeLayoutItem
    {
        get
        {
            var item = _lessons.CurrentTimeLayoutItem;
            return item == TimeLayoutItem.Empty ? null : ToInfo(item);
        }
    }

    /// <summary>下一节课开始时间（HH:mm）。</summary>
    public string? NextClassTime
    {
        get
        {
            var item = _lessons.NextClassTimeLayoutItem;
            return item == TimeLayoutItem.Empty ? null : FormatTime(item.StartTime);
        }
    }

    /// <summary>下一个课间开始时间（HH:mm）。</summary>
    public string? NextBreakingTime
    {
        get
        {
            var item = _lessons.NextBreakingTimeLayoutItem;
            return item == TimeLayoutItem.Empty ? null : FormatTime(item.StartTime);
        }
    }

    /// <summary>距上课剩余秒数。</summary>
    public double OnClassLeftTime => _lessons.OnClassLeftTime.TotalSeconds;

    /// <summary>距下课剩余秒数。</summary>
    public double OnBreakingTimeLeftTime => _lessons.OnBreakingTimeLeftTime.TotalSeconds;

    /// <summary>是否启用课表。</summary>
    public bool IsClassPlanEnabled => _lessons.IsClassPlanEnabled;

    /// <summary>是否已加载课表。</summary>
    public bool IsClassPlanLoaded => _lessons.IsClassPlanLoaded;

    /// <summary>是否已确定当前时间点。</summary>
    public bool IsLessonConfirmed => _lessons.IsLessonConfirmed;

    /// <summary>当前课表信息。</summary>
    public ScriptClassPlanInfo? CurrentClassPlan
    {
        get
        {
            var plan = _lessons.CurrentClassPlan;
            return plan == null
                ? null
                : new ScriptClassPlanInfo
                {
                    Name = plan.Name,
                    Date = _exactTimeService.GetCurrentLocalDateTime().ToString("yyyy-MM-dd")
                };
        }
    }

    /// <summary>本周多周轮换周数。</summary>
    public int[] MultiWeekRotation => _lessons.MultiWeekRotation.ToArray();

    /// <summary>
    /// 根据日期字符串获取当天的课表。
    /// </summary>
    /// <param name="dateString">日期字符串。</param>
    /// <returns>课表信息，找不到时为 null。</returns>
    public ScriptClassPlanInfo? GetClassPlanByDate(string dateString)
    {
        if (!DateTime.TryParse(dateString, out var date))
        {
            return null;
        }

        var plan = _lessons.GetClassPlanByDate(date);
        return plan == null
            ? null
            : new ScriptClassPlanInfo { Name = plan.Name, Date = date.ToString("yyyy-MM-dd") };
    }

    private TimeLayoutItem? PreviousClassItem()
    {
        var layout = Layout;
        if (layout == null)
        {
            return null;
        }

        var now = _exactTimeService.GetCurrentLocalDateTime().TimeOfDay;
        return layout.Layouts.Where(i => i.TimeType == 0 && i.EndTime < now).LastOrDefault();
    }

    private string? SubjectIdAt(TimeLayoutItem? item)
    {
        var layout = Layout;
        var plan = ClassPlan;
        if (layout == null || plan == null || item == null || item == TimeLayoutItem.Empty)
        {
            return null;
        }

        var index = layout.Layouts.IndexOf(item);
        if (index < 0)
        {
            return null;
        }

        var classItems = layout.Layouts.Where(t => t.TimeType == 0).ToList();
        var classIndex = classItems.IndexOf(item);
        if (classIndex < 0 || classIndex >= plan.Classes.Count)
        {
            return null;
        }

        var id = plan.Classes[classIndex].SubjectId;
        return id == Guid.Empty ? null : id.ToString();
    }

    private static string? SubjectName(Subject? subject)
    {
        return subject == null || subject == Subject.Fallback || string.IsNullOrEmpty(subject.Name)
            ? null
            : subject.Name;
    }

    private static string FormatTime(TimeSpan time) => $"{(int)time.TotalHours:00}:{time.Minutes:00}";

    private static ScriptTimeLayoutItemInfo ToInfo(TimeLayoutItem item) => new()
    {
        StartTime = FormatTime(item.StartTime),
        EndTime = FormatTime(item.EndTime),
        TimeType = item.TimeType,
        BreakName = item.BreakNameText
    };
}

/// <summary>
/// 脚本 <c>window</c> 全局对象的宿主实现。
/// </summary>
public sealed class WindowFacade
{
    private readonly IWindowRuleService _windowRuleService;

    /// <summary>
    /// 初始化一个 <see cref="WindowFacade"/> 实例。
    /// </summary>
    public WindowFacade(IWindowRuleService windowRuleService)
    {
        _windowRuleService = windowRuleService;
    }

    private HWND Hwnd => _windowRuleService.ForegroundHwnd;

    /// <summary>前台窗口标题。</summary>
    public string? Title
    {
        get
        {
            try
            {
                if (Hwnd == IntPtr.Zero)
                {
                    return null;
                }

                using var buffer = new DisposablePWSTR(512);
                GetWindowText(Hwnd, buffer.PWSTR, 512);
                return buffer.ToString();
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>前台窗口类名。</summary>
    public string? ClassName
    {
        get
        {
            try
            {
                if (Hwnd == IntPtr.Zero)
                {
                    return null;
                }

                using var buffer = new DisposablePWSTR(512);
                GetClassName(Hwnd, buffer.PWSTR, 512);
                return buffer.ToString();
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>前台窗口进程名。</summary>
    public string? ProcessName
    {
        get
        {
            try
            {
                if (Hwnd == IntPtr.Zero)
                {
                    return null;
                }

                uint pid = 0;
                unsafe
                {
                    GetWindowThreadProcessId(Hwnd, &pid);
                }

                return Process.GetProcessById((int)pid).ProcessName;
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>前台窗口状态。</summary>
    public string State
    {
        get
        {
            if (IsForegroundFullscreen())
            {
                return "fullscreen";
            }

            if (IsForegroundMaximized())
            {
                return "maximized";
            }

            if (IsForegroundMinimized())
            {
                return "minimized";
            }

            return "normal";
        }
    }

    /// <summary>前台窗口是否最大化。</summary>
    public bool IsForegroundMaximized()
    {
        try
        {
            return Hwnd != IntPtr.Zero && IsZoomed(Hwnd) && !IsForegroundFullscreen();
        }
        catch
        {
            return false;
        }
    }

    /// <summary>前台窗口是否全屏。</summary>
    public bool IsForegroundFullscreen()
    {
        try
        {
            return NativeWindowHelper.IsForegroundFullScreen(GetScreen()!);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>前台窗口是否最小化。</summary>
    public bool IsForegroundMinimized()
    {
        try
        {
            return Hwnd != IntPtr.Zero && IsIconic(Hwnd);
        }
        catch
        {
            return false;
        }
    }

    private static Screen? GetScreen()
    {
        try
        {
            var vm = App.GetService<MainViewModel>();
            var index = vm.Settings.WindowDockingMonitorIndex;
            return index >= 0 && index < Screen.AllScreens.Length ? Screen.AllScreens[index] : Screen.PrimaryScreen;
        }
        catch
        {
            return Screen.PrimaryScreen;
        }
    }
}

/// <summary>
/// 脚本 <c>weather</c> 全局对象的宿主实现。
/// </summary>
public sealed class WeatherFacade
{
    private readonly IWeatherService _weatherService;
    private readonly SettingsService _settingsService;

    /// <summary>
    /// 初始化一个 <see cref="WeatherFacade"/> 实例。
    /// </summary>
    public WeatherFacade(IWeatherService weatherService, SettingsService settingsService)
    {
        _weatherService = weatherService;
        _settingsService = settingsService;
    }

    private WeatherInfo Weather => _settingsService.Settings.LastWeatherInfo;

    /// <summary>天气是否已刷新。</summary>
    public bool IsRefreshed => _weatherService.IsWeatherRefreshed;

    /// <summary>当前天气代码。</summary>
    public string Current => Weather.Current.Weather;

    /// <summary>当前天气文本。</summary>
    public string CurrentText => _weatherService.GetWeatherTextByCode(Current);

    /// <summary>降水剩余分钟数。</summary>
    public int RainRemainingMinutes => Weather.Minutely.Precipitation.RainRemainingMinutes;

    /// <summary>气象预警列表。</summary>
    public ScriptWeatherAlertInfo[] Alerts => Weather.Alerts
        .Select(x => new ScriptWeatherAlertInfo { Title = x.Title, Detail = x.Detail })
        .ToArray();

    /// <summary>
    /// 判断降水是否在指定分钟内开始（或结束）。
    /// </summary>
    /// <param name="minutes">分钟数。</param>
    /// <param name="remaining">是否判断降水结束剩余时间。</param>
    public bool RainIn(double minutes, bool remaining)
    {
        var baseTime = (remaining ? -1.0 : 1.0) * RainRemainingMinutes;
        return baseTime > 0 && baseTime <= minutes;
    }

    /// <summary>
    /// 判断是否存在匹配的气象预警。
    /// </summary>
    /// <param name="text">匹配文本。</param>
    /// <param name="regex">是否使用正则表达式。</param>
    public bool HasAlert(string text, bool regex)
    {
        return _weatherService.IsWeatherRefreshed && Weather.Alerts.Exists(x => IsMatch(x.Title, text, regex));
    }

    /// <summary>
    /// 判断当前天气是否匹配给定的代码或文本。
    /// </summary>
    /// <param name="codeOrText">天气代码或文本。</param>
    public bool IsWeather(string codeOrText)
    {
        if (!_weatherService.IsWeatherRefreshed)
        {
            return false;
        }

        return Current == codeOrText || _weatherService.GetWeatherTextByCode(Current) == codeOrText;
    }

    private static bool IsMatch(string value, string pattern, bool regex)
    {
        if (!regex)
        {
            return value == pattern;
        }

        try
        {
            return Regex.Match(value, pattern).Success;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// 脚本 <c>time</c> 全局对象的宿主实现。
/// </summary>
public sealed class TimeFacade
{
    private readonly IExactTimeService _exactTimeService;

    /// <summary>
    /// 初始化一个 <see cref="TimeFacade"/> 实例。
    /// </summary>
    public TimeFacade(IExactTimeService exactTimeService)
    {
        _exactTimeService = exactTimeService;
    }

    /// <summary>获取当前本地时间。</summary>
    public DateTime Now() => _exactTimeService.GetCurrentLocalDateTime();

    /// <summary>获取当前本地时间的 Unix 毫秒时间戳。</summary>
    public double NowMs() => new DateTimeOffset(_exactTimeService.GetCurrentLocalDateTime()).ToUnixTimeMilliseconds();
}

/// <summary>
/// 脚本 <c>app</c> 全局对象的宿主实现。
/// </summary>
public sealed class AppFacade
{
    /// <summary>应用版本。</summary>
    public string Version => App.AppVersionLong;

    /// <summary>退出应用。</summary>
    public void Quit() => App.Current.Stop();

    /// <summary>重启应用。</summary>
    /// <param name="quiet">是否静默重启。</param>
    public void Restart(bool quiet) => App.Current.Restart(quiet);
}

/// <summary>
/// 脚本中的时间点信息。
/// </summary>
public sealed class ScriptTimeLayoutItemInfo
{
    /// <summary>开始时间（HH:mm）。</summary>
    public string StartTime { get; set; } = "";

    /// <summary>结束时间（HH:mm）。</summary>
    public string EndTime { get; set; } = "";

    /// <summary>时间点类型。</summary>
    public int TimeType { get; set; }

    /// <summary>课间名称。</summary>
    public string BreakName { get; set; } = "";
}

/// <summary>
/// 脚本中的课表信息。
/// </summary>
public sealed class ScriptClassPlanInfo
{
    /// <summary>课表名称。</summary>
    public string Name { get; set; } = "";

    /// <summary>课表日期（yyyy-MM-dd）。</summary>
    public string Date { get; set; } = "";
}

/// <summary>
/// 脚本中的气象预警信息。
/// </summary>
public sealed class ScriptWeatherAlertInfo
{
    /// <summary>预警标题。</summary>
    public string Title { get; set; } = "";

    /// <summary>预警详情。</summary>
    public string Detail { get; set; } = "";
}
