using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Models.Notification;
using ClassIsland.Services.NotificationProviders;
using ClassIsland.Shared.Enums;
using Jint.Native;
using Microsoft.Extensions.Logging;
using TimeCrontab;

namespace ClassIsland.Services.Scripting;

/// <summary>
/// 向脚本暴露的宿主 API 桥接。所有调用都运行在脚本调度线程上。
/// </summary>
public class ScriptApiBridge
{
    private readonly ILogger _logger;
    private readonly ScriptRuntimeService _runtime;
    private readonly ScriptDocument _document;
    private readonly INotificationHostService _notificationHostService;
    private readonly SettingsService _settingsService;
    private readonly IUriNavigationService _uriNavigationService;
    private readonly SignalTriggerHandlerService _signalTriggerHandlerService;

    /// <summary>
    /// 初始化一个 <see cref="ScriptApiBridge"/> 实例。
    /// </summary>
    public ScriptApiBridge(ILogger logger, ScriptRuntimeService runtime, ScriptDocument document,
        INotificationHostService notificationHostService, SettingsService settingsService,
        IUriNavigationService uriNavigationService,
        SignalTriggerHandlerService signalTriggerHandlerService)
    {
        _logger = logger;
        _runtime = runtime;
        _document = document;
        _notificationHostService = notificationHostService;
        _settingsService = settingsService;
        _uriNavigationService = uriNavigationService;
        _signalTriggerHandlerService = signalTriggerHandlerService;
    }

    /// <summary>
    /// 输出脚本日志。
    /// </summary>
    public void Log(params object[] args)
    {
        var message = string.Join(" ", args.Select(x => x?.ToString() ?? "null"));
        _logger.LogInformation("{}", message);
        _runtime.RaiseLog(_document.FilePath, "info", message);
    }

    /// <summary>
    /// 记录脚本内部未捕获的错误。
    /// </summary>
    public void LogError(string message)
    {
        _logger.LogWarning("脚本“{File}”内部错误：{}", _document.FilePath, message);
        _runtime.RaiseLog(_document.FilePath, "error", message);
    }

    /// <summary>
    /// 运行文件、命令或网址。逻辑与 <see cref="ActionHandlers.RunActionHandler"/> 保持一致。
    /// </summary>
    public void Run(string value, string? args)
    {
        args ??= "";

        // 文件(夹)
        {
            var path = value.Replace("\"", "");
            if (File.Exists(path) || Directory.Exists(path))
            {
                try
                {
                    Process.Start(new ProcessStartInfo()
                    {
                        FileName = value,
                        Arguments = args,
                        UseShellExecute = true
                    });
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "打开文件(夹)失败。");
                }
            }
        }

        // cmd 命令
        try
        {
            Process process = new()
            {
                StartInfo = new()
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c {value}",
                    RedirectStandardError = true
                }
            };
            process.Start();

            if (string.IsNullOrEmpty(process.StandardError.ReadToEnd()))
                return;
        }
        catch { }

        // 网页
        {
            var path = value;
            if (value.Contains('.') && !value.Contains("://"))
            {
                path = "http://" + path;
            }
            if (Uri.TryCreate(path, UriKind.Absolute, out Uri uri))
            {
                Process.Start("explorer.exe", uri.AbsoluteUri);
                return;
            }
        }

        throw new InvalidOperationException($"未能运行“{value}”：未匹配到合适的运行方式。");
    }

    /// <summary>
    /// 显示一条提醒。
    /// </summary>
    public void Notify(string mask, string content, bool speech, bool topmost, bool sound, bool effect,
        double contentDuration, double maskDuration)
    {
        var request = new NotificationRequest
        {
            MaskContent = NotificationContent.CreateTwoIconsMask(mask, hasRightIcon: false, factory: x =>
            {
                x.IsSpeechEnabled = speech;
                if (maskDuration > 0)
                {
                    x.Duration = TimeSpan.FromSeconds(maskDuration);
                }
            }),
            OverlayContent = string.IsNullOrEmpty(content)
                ? null
                : NotificationContent.CreateSimpleTextContent(content, factory: x =>
                {
                    x.IsSpeechEnabled = speech;
                    if (contentDuration > 0)
                    {
                        x.Duration = TimeSpan.FromSeconds(contentDuration);
                    }
                }),
            RequestNotificationSettings =
            {
                IsSettingsEnabled = topmost || sound || effect,
                IsNotificationTopmostEnabled = topmost,
                IsNotificationSoundEnabled = sound,
                IsNotificationEffectEnabled = effect
            }
        };

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(new Action(() => Show(request)));
        }
        else
        {
            Show(request);
        }
    }

    private void Show(NotificationRequest request)
    {
        try
        {
            _notificationHostService.ShowNotification(request, Guid.Empty, Guid.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "显示脚本提醒失败。");
        }
    }

    /// <summary>
    /// 显示天气提醒。直接调用天气提醒提供方。
    /// </summary>
    public void WeatherNotify(int kind)
    {
        try
        {
            var provider = _notificationHostService.NotificationProviders
                .Select(x => x.ProviderInstance)
                .OfType<WeatherNotificationProvider>()
                .FirstOrDefault();
            if (provider == null)
            {
                _logger.LogWarning("未找到天气提醒提供方。");
                return;
            }

            provider.Notify(kind);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "显示脚本天气提醒失败。");
        }
    }

    /// <summary>
    /// 广播一个信号。
    /// </summary>
    public void Broadcast(string name, bool revert)
    {
        _signalTriggerHandlerService.EmitSignal(name, revert);
    }

    /// <summary>
    /// 应用主题叠层。
    /// </summary>
    public void SetTheme(int mode, string? primary, string? secondary)
    {
        AddOverlay("Theme", mode);
        if (!string.IsNullOrEmpty(primary))
        {
            AddOverlay("ColorSource", 0);
            AddOverlay("PrimaryColor", ParseColor(primary!));
            AddOverlay("SecondaryColor", ParseColor(string.IsNullOrEmpty(secondary) ? primary! : secondary!));
        }
    }

    /// <summary>
    /// 清除主题叠层。
    /// </summary>
    public void ClearTheme()
    {
        RemoveOverlay("Theme");
        RemoveOverlay("ColorSource");
        RemoveOverlay("PrimaryColor");
        RemoveOverlay("SecondaryColor");
    }

    /// <summary>设置窗口停靠位置叠层。</summary>
    public void SetWindowDockingLocation(int value) => AddOverlay("WindowDockingLocation", value);

    /// <summary>清除窗口停靠位置叠层。</summary>
    public void ClearWindowDockingLocation() => RemoveOverlay("WindowDockingLocation");

    /// <summary>设置窗口层级叠层。</summary>
    public void SetWindowLayer(int value) => AddOverlay("WindowLayer", value);

    /// <summary>清除窗口层级叠层。</summary>
    public void ClearWindowLayer() => RemoveOverlay("WindowLayer");

    /// <summary>设置窗口向右偏移叠层。</summary>
    public void SetWindowDockingOffsetX(int value) => AddOverlay("WindowDockingOffsetX", value);

    /// <summary>清除窗口向右偏移叠层。</summary>
    public void ClearWindowDockingOffsetX() => RemoveOverlay("WindowDockingOffsetX");

    /// <summary>设置窗口向下偏移叠层。</summary>
    public void SetWindowDockingOffsetY(int value) => AddOverlay("WindowDockingOffsetY", value);

    /// <summary>清除窗口向下偏移叠层。</summary>
    public void ClearWindowDockingOffsetY() => RemoveOverlay("WindowDockingOffsetY");

    /// <summary>切换组件配置方案叠层。</summary>
    public void SwitchComponentConfig(string name) => AddOverlay("CurrentComponentConfig", name);

    /// <summary>清除组件配置方案叠层。</summary>
    public void ClearComponentConfig() => RemoveOverlay("CurrentComponentConfig");

    /// <summary>
    /// 导航到指定 Uri。
    /// </summary>
    public void Navigate(string uri)
    {
        var dispatcher = Application.Current?.Dispatcher;
        void Action()
        {
            try
            {
                _uriNavigationService.NavigateWrapped(new Uri(uri), out _);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "脚本导航失败：{Uri}", uri);
            }
        }

        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(new Action(Action));
        }
        else
        {
            Action();
        }
    }

    /// <summary>
    /// 延迟指定毫秒并在调度线程上恢复脚本。不阻塞引擎/调度线程。
    /// </summary>
    /// <param name="milliseconds">延迟毫秒数。</param>
    /// <param name="resolve">JS Promise 的 resolve 回调。</param>
    public void Sleep(double milliseconds, JsValue resolve)
    {
        var token = _runtime.CancellationToken;
        Task.Delay(TimeSpan.FromMilliseconds(Math.Max(0, milliseconds)), token).ContinueWith(_ =>
        {
            if (token.IsCancellationRequested)
            {
                return;
            }

            _runtime.Post(() =>
            {
                try
                {
                    _document.Engine?.Invoke(resolve);
                    _document.Engine?.Advanced.ProcessTasks();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "脚本“{File}”恢复 sleep 失败。", _document.FilePath);
                }
            });
        }, TaskScheduler.Default);
    }

    /// <summary>
    /// 注册 cron 触发器。
    /// </summary>
    public void RegisterCron(string expression, int handlerId)
    {
        var crontab = Crontab.TryParse(expression);
        if (crontab == null)
        {
            _logger.LogWarning("脚本“{File}”注册了无效的 cron 表达式：{Expression}", _document.FilePath, expression);
            return;
        }

        var now = _runtime.ExactTime.GetCurrentLocalDateTime();
        _document.CronEntries.Add(new ScriptCronEntry
        {
            Expression = expression,
            Crontab = crontab,
            HandlerId = handlerId,
            Next = now + crontab.GetSleepTimeSpan(now)
        });
    }

    /// <summary>
    /// 注册“特定时间点前”触发器。
    /// </summary>
    public void RegisterPreTimePoint(string state, double seconds, int handlerId)
    {
        var timeState = Enum.TryParse<TimeState>(state, true, out var parsed) ? parsed : TimeState.OnClass;
        _document.PreTimePointEntries.Add(new ScriptPreTimePointEntry
        {
            State = timeState,
            Seconds = seconds,
            HandlerId = handlerId,
            LastCheck = _runtime.ExactTime.GetCurrentLocalDateTime()
        });
    }

    private static Color ParseColor(string value)
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString(value)!;
        }
        catch
        {
            return Colors.DodgerBlue;
        }
    }

    private void AddOverlay(string binding, object? value)
    {
        var dispatcher = Application.Current?.Dispatcher;
        void Action()
        {
            try
            {
                _settingsService.AddSettingsOverlay(_document.FilePath, binding, value);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "脚本添加设置叠层失败：{Binding}", binding);
            }
        }

        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(new Action(Action), DispatcherPriority.Render);
        }
        else
        {
            Action();
        }
    }

    private void RemoveOverlay(string binding)
    {
        var dispatcher = Application.Current?.Dispatcher;
        void Action()
        {
            try
            {
                _settingsService.RemoveSettingsOverlay(_document.FilePath, binding);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "脚本移除设置叠层失败：{Binding}", binding);
            }
        }

        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(new Action(Action), DispatcherPriority.Render);
        }
        else
        {
            Action();
        }
    }
}
