using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Management;
using System.Threading;
using System.Threading.Tasks;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Models;
using ClassIsland.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Timer = System.Timers.Timer;

namespace ClassIsland.Services;

public class ProcessGuardService(ILogger<ProcessGuardService> logger, SettingsService settingsService) : BackgroundService
{
    private ILogger<ProcessGuardService> Logger { get; } = logger;

    private SettingsService SettingsService { get; } = settingsService;

    private Timer Timer { get; } = new()
    {
        Interval = 5000
    };

    private static readonly string[] InternalFlags = ["-psmk", "-udt", "-urt", "-m"];

    private static readonly HashSet<string> DefaultFamilyNames =
        new(["ClassIsland", "ClassIsland.Desktop", "ClassIsland.App"], StringComparer.OrdinalIgnoreCase);

    private readonly HashSet<string> _notified = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    private readonly int _currentPid = Process.GetCurrentProcess().Id;

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Timer.Elapsed += TimerOnElapsed;
        Timer.Start();

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(2000, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            TimerOnElapsed(null, null!);
        }, stoppingToken);

        return Task.CompletedTask;
    }

    private void TimerOnElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        try
        {
            lock (_gate)
            {
                Tick();
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "进程守护检测失败。");
        }
    }

    private void Tick()
    {
        var settings = SettingsService.Settings;
        if (!settings.IsProcessGuardEnabled)
        {
            return;
        }

        var names = ParseNames(settings.ProcessGuardProcessNames);
        if (names.Count == 0)
        {
            return;
        }

        var detected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names)
        {
            Process[] candidates;
            try
            {
                candidates = Process.GetProcessesByName(name);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "枚举进程 {Name} 失败。", name);
                continue;
            }

            foreach (var candidate in candidates)
            {
                using (candidate)
                {
                    try
                    {
                        if (HandleCandidate(name, candidate, settings))
                        {
                            detected.Add(name);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning(ex, "处理进程 {Name} 失败。", name);
                    }
                }
            }
        }

        _notified.RemoveWhere(n => !detected.Contains(n));
    }

    private bool HandleCandidate(string configuredName, Process candidate, Settings settings)
    {
        int id;
        try
        {
            id = candidate.Id;
        }
        catch (InvalidOperationException)
        {
            return false;
        }

        if (id == _currentPid)
        {
            return false;
        }

        if (!IsFamilyTarget(configuredName, candidate, settings.ProcessGuardKillThirdParty))
        {
            return false;
        }

        // 无法读取命令行时跳过：宁可漏杀，也不能误杀重启/更新产生的同族进程。
        if (!TryGetCommandLine(id, out var commandLine))
        {
            return false;
        }

        if (ContainsInternalFlag(commandLine))
        {
            return false;
        }

        if (_notified.Add(configuredName))
        {
            Notify("进程守护",
                $"检测到同类进程 {configuredName}（PID {id}）。" +
                (settings.ProcessGuardNotifyOnly ? "" : "已尝试结束该进程。"));
        }

        if (settings.ProcessGuardNotifyOnly)
        {
            return true;
        }

        try
        {
            candidate.Kill();
            Logger.LogInformation("已结束同类进程 {Name}（PID {Id}）。", configuredName, id);
        }
        catch (Win32Exception ex)
        {
            Logger.LogWarning(ex, "结束进程 {Name}（PID {Id}）失败：访问被拒绝，目标可能以管理员身份运行。", configuredName, id);
        }
        catch (InvalidOperationException ex)
        {
            Logger.LogWarning(ex, "结束进程 {Name}（PID {Id}）失败：进程已退出。", configuredName, id);
        }

        return true;
    }

    private bool IsFamilyTarget(string name, Process candidate, bool killThirdParty)
    {
        if (killThirdParty)
        {
            return true;
        }

        string? path = null;
        try
        {
            path = candidate.MainModule?.FileName;
        }
        catch
        {
            // 无法读取主模块（如权限不足），回退到默认同族进程名判断。
        }

        if (!string.IsNullOrEmpty(path))
        {
            try
            {
                var info = FileVersionInfo.GetVersionInfo(path);
                if (ContainsClassIsland(info.ProductName) ||
                    ContainsClassIsland(info.CompanyName) ||
                    ContainsClassIsland(path))
                {
                    return true;
                }
            }
            catch
            {
                if (ContainsClassIsland(path))
                {
                    return true;
                }
            }
        }

        return DefaultFamilyNames.Contains(name);
    }

    private static bool ContainsClassIsland(string? value)
        => value != null && value.IndexOf("ClassIsland", StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool ContainsInternalFlag(string commandLine)
    {
        foreach (var token in commandLine.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
        {
            var value = token.Trim('"');
            foreach (var flag in InternalFlags)
            {
                if (string.Equals(value, flag, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryGetCommandLine(int pid, out string commandLine)
    {
        commandLine = "";
        try
        {
            using var searcher = new ManagementObjectSearcher(
                $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {pid}");
            foreach (var obj in searcher.Get())
            {
                commandLine = obj["CommandLine"] as string ?? "";
                return true;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private static HashSet<string> ParseNames(string? raw)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in (raw ?? "").Split(';'))
        {
            var name = part.Trim();
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                name = name[..^4];
            }

            if (name.Length > 0)
            {
                set.Add(name);
            }
        }

        return set;
    }

    private void Notify(string title, string message)
    {
        try
        {
            IAppHost.Host?.Services.GetService<ITaskBarIconService>()
                ?.ShowNotification(title, message);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "显示进程守护通知失败。");
        }
    }
}
