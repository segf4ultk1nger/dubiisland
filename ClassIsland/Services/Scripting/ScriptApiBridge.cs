using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Models.Notification;
using Microsoft.Extensions.Logging;

namespace ClassIsland.Services.Scripting;

/// <summary>
/// 向脚本暴露的宿主 API 桥接。
/// </summary>
public class ScriptApiBridge
{
    private readonly ILogger _logger;
    private readonly INotificationHostService _notificationHostService;

    /// <summary>
    /// 初始化一个 <see cref="ScriptApiBridge"/> 实例。
    /// </summary>
    /// <param name="logger">日志记录器。</param>
    /// <param name="notificationHostService">提醒主机服务。</param>
    public ScriptApiBridge(ILogger logger, INotificationHostService notificationHostService)
    {
        _logger = logger;
        _notificationHostService = notificationHostService;
    }

    /// <summary>
    /// 输出脚本日志。
    /// </summary>
    /// <param name="args">要输出的参数。</param>
    public void Log(params object[] args)
    {
        _logger.LogInformation("{}", string.Join(" ", args.Select(x => x?.ToString() ?? "null")));
    }

    /// <summary>
    /// 记录脚本内部未捕获的错误。
    /// </summary>
    /// <param name="message">错误信息。</param>
    public void LogError(string message)
    {
        _logger.LogWarning("脚本内部错误：{}", message);
    }

    /// <summary>
    /// 运行文件、命令或网址。逻辑与 <see cref="ActionHandlers.RunActionHandler"/> 保持一致。
    /// </summary>
    /// <param name="value">要运行的目标。</param>
    /// <param name="args">参数。</param>
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
    /// 显示一条文本提醒。
    /// </summary>
    /// <param name="text">提醒文本。</param>
    public void Notify(string text)
    {
        var request = new NotificationRequest
        {
            MaskContent = NotificationContent.CreateTwoIconsMask(text, hasRightIcon: false),
            OverlayContent = NotificationContent.CreateSimpleTextContent(text)
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
}
