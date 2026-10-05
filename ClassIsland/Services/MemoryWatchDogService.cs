using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using ClassIsland.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Timer = System.Timers.Timer;

namespace ClassIsland.Services;

public class MemoryWatchDogService(ILogger<MemoryWatchDogService> logger, SettingsService settingsService) : BackgroundService
{
    private ILogger<MemoryWatchDogService> Logger { get; } = logger;

    private SettingsService SettingsService { get; } = settingsService;

    private Timer Timer { get; } = new()
    {
        Interval = 60000
    };

    public static readonly long MemoryLimitBytes = 1500000000; // 1.5GB


    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Timer.Elapsed += TimerOnElapsed;
        Timer.Start();
        return Task.CompletedTask;
    }

    private void TimerOnElapsed(object? sender, ElapsedEventArgs e)
    {
        if (SettingsService.Settings.IsMemoryTrimEnabled)
        {
            try
            {
                // 「黑科技」：裁减工作集，让任务管理器数字立刻变小。仅观感优化，不减少真实占用。
                EmptyWorkingSet(new HANDLE(Process.GetCurrentProcess().Handle));
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "降低工作集失败。");
            }
        }

        var size = Process.GetCurrentProcess().PrivateMemorySize64;
        //Console.WriteLine(size);
        Logger.LogInformation("当前内存使用: {}", Helpers.StorageSizeHelper.FormatSize((ulong)size)+$"({size} Bytes)");
        if (size < MemoryLimitBytes) 
            return;
        Logger.LogCritical("达到内存使用上限！ {} / {}", Helpers.StorageSizeHelper.FormatSize((ulong)size)+$"({size} Bytes)", Helpers.StorageSizeHelper.FormatSize((ulong)MemoryLimitBytes)+$"({MemoryLimitBytes} Bytes)");
        //var startInfo = Process.GetCurrentProcess().StartInfo;
        var replaced = FrameworkCompat.ProcessPath.Replace(".dll", ".exe");
        var startInfo = new ProcessStartInfo(replaced)
        {
            Arguments = FrameworkCompat.JoinArguments("-q", "-m", "-psmk")
        };
        Process.Start(startInfo);
        //Process.Start(startInfo);
        AppBase.Current.Stop();
    }
}