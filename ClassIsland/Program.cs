using ClassIsland.Models;
using System;
using System.CommandLine.NamingConventionBinder;
using System.CommandLine;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ClassIsland.Core;
using ClassIsland.Core.Enums;
using ClassIsland.Services;
using System.Diagnostics;
using IccEvolved.UiAccess;

namespace ClassIsland;

internal static class Program
{
    internal const string UiAccessChildMarker = "-uiaccess-child";

    internal const string UiAccessHelperResourceName = "ClassIsland.UiAccess.IccEvolved.UiAccess.Helper.exe";
    internal const string UiAccessDllResourceName = "ClassIsland.UiAccess.IccEvolved.UiAccess.dll";

    [STAThread]
    private static void Main(string[] args)
    {
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        // 自解压 UIAccess 组件到应用目录，供超级置顶使用（失败则忽略，按“无 Helper”正常启动）。
        ExtractUiAccessPayload();
        MainAsync(args).GetAwaiter().GetResult();
    }

    /// <summary>把内嵌的 Helper 及依赖 DLL 解压到应用目录（exe 同目录）。</summary>
    private static void ExtractUiAccessPayload()
    {
        var dir = AppDomain.CurrentDomain.BaseDirectory;
        ExtractEmbeddedResource(UiAccessDllResourceName, Path.Combine(dir, "IccEvolved.UiAccess.dll"));
        ExtractEmbeddedResource(UiAccessHelperResourceName, Path.Combine(dir, "IccEvolved.UiAccess.Helper.exe"));
    }

    private static void ExtractEmbeddedResource(string resourceName, string targetPath)
    {
        var tempPath = targetPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using var stream = typeof(Program).Assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                return;
            }

            using (var file = File.Create(tempPath))
            {
                stream.CopyTo(file);
            }

            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }

            File.Move(tempPath, targetPath);
        }
        catch
        {
            // 应用目录不可写或文件被占用：忽略，超级置顶将不可用。
            try
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
            catch
            {
                // ignored
            }
        }
    }

    private static async Task MainAsync(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += DiagnosticService.ProcessDomainUnhandledException;
        AppBase.CurrentLifetime = ApplicationLifetime.EarlyLoading;

        // UIAccess 子进程特征：命令行带 marker（内含交接文件路径）→ 删除交接文件，跳过启用流程。
        var isUiAccessChild = TryGetUiAccessHandoff(args, out var handoffPath);
        if (isUiAccessChild && !string.IsNullOrEmpty(handoffPath))
        {
            try
            {
                if (File.Exists(handoffPath)) File.Delete(handoffPath);
            }
            catch
            {
                // ignored
            }
        }

        // 以 UIAccess 身份重启自身（超级置顶）：成功后由带 marker 的子进程接管，本进程退出。
        // --no-uiaccess 可禁用（调试用）。
        if (!isUiAccessChild && !args.Contains("--no-uiaccess") && IsUiAccessHelperAvailable() && ReadUiAccessEnabled())
        {
            var uiAccessOptions = new UiAccessOptions
            {
                Enabled = true,
                LaunchMode = LaunchMode.RelaunchSelf,
                MarkerArg = UiAccessChildMarker
            };
            uiAccessOptions.SystemIdentityChain.Clear();
            uiAccessOptions.SystemIdentityChain.Add(SystemIdentityMethod.Service);
            uiAccessOptions.SystemIdentityChain.Add(SystemIdentityMethod.Winlogon);
            var uiAccessResult = UiAccessLauncher.Launch(uiAccessOptions);
            if (uiAccessResult.Status == UiAccessStatus.Succeeded)
            {
                Environment.Exit(0);
            }
        }

        var parseArgs = args
            .Where(a => !a.StartsWith(UiAccessChildMarker, StringComparison.OrdinalIgnoreCase))
            .Where(a => !string.Equals(a, "--no-uiaccess", StringComparison.OrdinalIgnoreCase))
            .ToArray();

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
        command.Invoke(parseArgs);

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

    private static bool TryGetUiAccessHandoff(string[] args, out string handoffPath)
    {
        handoffPath = null;
        var prefix = UiAccessChildMarker + "=";
        foreach (var arg in args)
        {
            if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                handoffPath = arg.Substring(prefix.Length);
                return true;
            }

            if (string.Equals(arg, UiAccessChildMarker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 检查 UIAccess 组件（Helper 及其依赖的同目录 DLL）是否随包就绪。
    /// 缺失时跳过超级置顶，正常启动。
    /// </summary>
    internal static bool IsUiAccessHelperAvailable()
    {
        try
        {
            var dir = AppDomain.CurrentDomain.BaseDirectory;
            return File.Exists(Path.Combine(dir, "IccEvolved.UiAccess.Helper.exe"))
                   && File.Exists(Path.Combine(dir, "IccEvolved.UiAccess.dll"));
        }
        catch
        {
            return false;
        }
    }

    private static bool ReadUiAccessEnabled()
    {
        try
        {
            var path = Path.Combine(App.AppRootFolderPath, "Settings.json");
            if (!File.Exists(path)) return false;
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.TryGetProperty(nameof(Settings.IsUiAccessEnabled), out var property) &&
                   property.ValueKind == JsonValueKind.True;
        }
        catch
        {
            return false;
        }
    }
}
