using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using ClassIsland.Services;
using ClassIsland.Shared;
using Microsoft.Extensions.Logging;

namespace ClassIsland.Helpers;

/// <summary>
/// DEBUG 内存诊断：在关键节点打印托管堆 / 私有内存 / 工作集，以及 MainWindow、工具窗口的存活情况，
/// 便于不开 profiler 时先粗定位。Release 下为空实现。
/// </summary>
internal static class MemoryDiagnostics
{
    public static void Log(string tag)
    {
#if DEBUG
        try
        {
            var process = Process.GetCurrentProcess();
            var managed = GC.GetTotalMemory(false) / 1024.0 / 1024.0;
            var priv = process.PrivateMemorySize64 / 1024.0 / 1024.0;
            var working = process.WorkingSet64 / 1024.0 / 1024.0;
            var main = Application.Current?.MainWindow is ClassIsland.MainWindow ? "MainWindow=1" : "MainWindow=0";
            var text =
                $"[MEM] {tag}: managed={managed:F1}MB private={priv:F1}MB working={working:F1}MB | {main} | {DescribeToolWindows()}";

            Debug.WriteLine(text);
            IAppHost.TryGetService<ILoggerFactory>()?.CreateLogger("MemoryDiagnostics")?.LogInformation("{Text}", text);
        }
        catch
        {
            // 诊断失败不影响主流程。
        }
#endif
    }

    private static string DescribeToolWindows()
    {
        var manager = IAppHost.TryGetService<ToolWindowManager>();
        if (manager == null)
        {
            return "toolWindows=?";
        }

        var live = manager.LiveWindowTypes;
        return live.Count == 0
            ? "toolWindows=none"
            : "toolWindows=" + string.Join("+", live.Select(t => t.Name));
    }
}
