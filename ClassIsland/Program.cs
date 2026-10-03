using ClassIsland.Models;
using System;
using System.CommandLine.NamingConventionBinder;
using System.CommandLine;
using System.IO;
using System.IO.Pipes;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ClassIsland.Core;
using ClassIsland.Core.Enums;
using ClassIsland.Services;
using System.Diagnostics;

namespace ClassIsland;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        MainAsync(args).GetAwaiter().GetResult();
    }

    private static async Task MainAsync(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += DiagnosticService.ProcessDomainUnhandledException;
        AppBase.CurrentLifetime = ApplicationLifetime.EarlyLoading;

        var command = new RootCommand
        {
            new Option<string>(["--updateReplaceTarget", "-urt"], "更新时要替换的文件"),
            new Option<string>(["--updateDeleteTarget", "-udt"], "更新完成要删除的文件"),
            new Option<string>(["--uri"], "启动时要导航到的Uri"),
            new Option<bool>(["--waitMutex", "-m"], "重复启动应用时，等待上一个实例退出而非直接退出应用。"),
            new Option<bool>(["--quiet", "-q"], "静默启动，启动时不显示Splash，并且启动后10秒内不显示任何通知。"),
            new Option<bool>(["-prevSessionMemoryKilled", "-psmk"], "上个会话因MLE结束。"),
            new Option<bool>(["-disableManagement", "-dm"], "在本次会话禁用集控。"),
            new Option<string>(["-externalPluginPath", "-epp"], "外部插件路径"),
            new Option<bool>(["--verbose", "-v"], "启用详细输出"),
            new Option<bool>(["--showOssWatermark", "-ossw"], "显示开源地址水印"),
            new Option<bool>(["--recovery", "-r"], "启动时进入恢复模式"),
            new Option<bool>(["--diagnostic", "-d"], "启用诊断模式"),
            new Option<bool>(["--safe", "-s"], "启用安全模式")
        };
        command.Handler = CommandHandler.Create((ApplicationCommand c) => { App.ApplicationCommand = c; });
        command.Invoke(args);

        if (App.ApplicationCommand.Diagnostic)
        {
            AllocConsole();
        }

        var mutex = new Mutex(true, "ClassIsland.Lock", out var createNew);

        if (!createNew)
        {
            if (App.ApplicationCommand.WaitMutex)
            {
                try
                {
                    mutex?.WaitOne();
                }
                catch
                {
                    // ignored
                }
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(App.ApplicationCommand.Uri))
                {
                    ProcessUriNavigation();
                }
            }
        }

        void SetProcessPriority(uint priority)
        {
            Process.GetCurrentProcess().PriorityClass = priority switch
            {
                0 => ProcessPriorityClass.Idle,
                1 => ProcessPriorityClass.BelowNormal,
                2 => ProcessPriorityClass.Normal,
                3 => ProcessPriorityClass.AboveNormal,
                4 => ProcessPriorityClass.High,
                5 => ProcessPriorityClass.RealTime,
                _ => ProcessPriorityClass.Normal,
            };
        }

        if (Environment.GetEnvironmentVariable("ClassIsland_ProcessPriority") is string priorityStr && uint.TryParse(priorityStr, out uint priority))
        {
            SetProcessPriority(priority);
        }
        else SetProcessPriority(2); //If not set or invalid, default to Normal priority (2).


        var app = new App()
        {
            Mutex = mutex,
            IsMutexCreateNew = createNew
        };
        app.InitializeComponent();
        app.Run();
        return;

        static void ProcessUriNavigation()
        {
            try
            {
                using var client = new NamedPipeClientStream(".", "ClassIsland.Uri", PipeDirection.Out);
                client.Connect(2000);
                using var writer = new StreamWriter(client, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), 1024, leaveOpen: true)
                {
                    AutoFlush = true
                };
                writer.WriteLine(App.ApplicationCommand.Uri);
                Environment.Exit(0);
            }
            catch
            {
                // ignored
            }
        }
    }
}
