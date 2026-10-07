using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Helpers.Native;
using ClassIsland.Models;
using ClassIsland.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ClassIsland.Services;

public class InjectionGuardService(ILogger<InjectionGuardService> logger, SettingsService settingsService) : BackgroundService
{
    private ILogger<InjectionGuardService> Logger { get; } = logger;

    private SettingsService SettingsService { get; } = settingsService;

    private static readonly string[] DefaultRules =
    [
        "Nahimic", "A-Volute", "SonicStudio", "Sonic Studio", "RTSS", "RTSSHooks", "Overwolf", "GameOverlay"
    ];

    // 委托与事件必须被 root，否则 GC 回收后加载器回调会崩溃进程。
    private static readonly AutoResetEvent WakeEvent = new(false);

    private static readonly InjectionGuardNative.LdrDllNotification NotificationCallback = OnDllNotification;

    private static void OnDllNotification(uint reason, IntPtr notificationData, IntPtr context) => WakeEvent.Set();

    private readonly HashSet<(IntPtr Base, string File)> _knownModules = [];
    private readonly HashSet<string> _notifiedModules = new(StringComparer.OrdinalIgnoreCase);
    private bool _registered;
    private IntPtr _cookie;

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var status = InjectionGuardNative.LdrRegisterDllNotification(0, Marshal.GetFunctionPointerForDelegate(NotificationCallback), IntPtr.Zero, out _cookie);
            _registered = status == 0;
            if (!_registered)
            {
                Logger.LogWarning("注册 DLL 加载通知失败，状态码：0x{Status:X8}", status);
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "注册 DLL 加载通知失败。");
        }

        _ = Task.Run(() => WorkerLoop(stoppingToken), stoppingToken);
        return Task.CompletedTask;
    }

    private void WorkerLoop(CancellationToken stoppingToken)
    {
        var waitHandles = new WaitHandle[] { WakeEvent, stoppingToken.WaitHandle };
        while (!stoppingToken.IsCancellationRequested)
        {
            WaitHandle.WaitAny(waitHandles, 5000);
            if (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                ScanModules();
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "扫描进程模块失败。");
            }
        }
    }

    private void ScanModules()
    {
        var settings = SettingsService.Settings;
        var current = new HashSet<(IntPtr Base, string File)>();
        using (var process = Process.GetCurrentProcess())
        {
            foreach (ProcessModule module in process.Modules)
            {
                try
                {
                    var file = module.FileName;
                    if (!string.IsNullOrEmpty(file))
                    {
                        current.Add((module.BaseAddress, file));
                    }
                }
                catch
                {
                    // 部分模块信息无法读取，忽略。
                }
            }
        }

        foreach (var module in current)
        {
            if (!_knownModules.Add(module))
            {
                continue;
            }

            if (settings.IsInjectionGuardEnabled && MatchesRule(Path.GetFileName(module.File), module.File))
            {
                HandleMatch(module.Base, module.File, settings);
            }
        }

        _knownModules.RemoveWhere(m => !current.Contains(m));
    }

    private void HandleMatch(IntPtr baseAddress, string path, Settings settings)
    {
        var name = Path.GetFileName(path);
        Logger.LogWarning("检测到第三方 DLL 注入：{Name}（{Path}）", name, path);

        if (_notifiedModules.Add(path))
        {
            try
            {
                IAppHost.Host?.Services.GetService<ITaskBarIconService>()
                    ?.ShowNotification("注入防护", $"检测到第三方 DLL 注入：{name}。如需彻底解决，请在对应软件中排除本应用，或停用相关服务。");
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "显示注入防护通知失败。");
            }
        }

        if (!settings.InjectionGuardUnloadEnabled)
        {
            return;
        }

        try
        {
            var result = InjectionGuardNative.TryUnloadModule(baseAddress);
            Logger.LogInformation("尝试卸载注入模块 {Name}：{Result}", name, result ? "成功" : "失败");
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "卸载注入模块 {Name} 失败。", name);
        }
    }

    private bool MatchesRule(string fileName, string path)
    {
        foreach (var rule in DefaultRules)
        {
            if (ContainsRule(fileName, path, rule))
            {
                return true;
            }
        }

        foreach (var raw in (SettingsService.Settings.InjectionGuardRules ?? "").Split(';'))
        {
            var rule = raw.Trim();
            if (rule.Length > 0 && ContainsRule(fileName, path, rule))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsRule(string fileName, string path, string rule)
        => fileName.IndexOf(rule, StringComparison.OrdinalIgnoreCase) >= 0 ||
           path.IndexOf(rule, StringComparison.OrdinalIgnoreCase) >= 0;

    public override void Dispose()
    {
        if (_registered)
        {
            try
            {
                InjectionGuardNative.LdrUnregisterDllNotification(_cookie);
            }
            catch
            {
                // ignored
            }

            _registered = false;
        }

        base.Dispose();
    }
}
