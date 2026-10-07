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
using ClassIsland.Core.Converters;
using ClassIsland.Core.Enums;
using ClassIsland.Helpers;
using ClassIsland.Services;
using ClassIsland.Shared.Helpers;
using ClassIsland.Shared.JsonConverters;
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
        // 伪装进程名：设置开启时从改名后的 exe 副本重启自身。必须在互斥量创建前执行，
        // 否则子进程会看到互斥量已被占用而误判为重复启动。任意失败则按原样正常启动。
        if (TryRelaunchAsDisguisedProcess(args))
        {
            return;
        }

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

    /// <summary>
    /// 伪装进程名：当设置为开启且当前进程名与伪装名不一致时，把自身复制为伪装名的 exe 并重启。
    /// 成功启动子进程后返回 true（调用方应立即结束本进程）；未启用或失败时返回 false，按原样启动。
    /// </summary>
    private static bool TryRelaunchAsDisguisedProcess(string[] args)
    {
        try
        {
            var settingsPath = Path.Combine(AppContext.BaseDirectory, "Settings.json");
            if (!File.Exists(settingsPath))
            {
                settingsPath = Path.Combine(Environment.CurrentDirectory, "Settings.json");
            }

            if (!File.Exists(settingsPath))
            {
                return false;
            }

            bool enabled;
            string disguisedName;
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(settingsPath));
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    return false;
                }

                enabled = root.TryGetProperty("IsRandomProcessNameEnabled", out var enabledElement) &&
                          enabledElement.ValueKind == JsonValueKind.True;
                disguisedName = root.TryGetProperty("DisguisedProcessName", out var nameElement) &&
                                nameElement.ValueKind == JsonValueKind.String
                    ? nameElement.GetString() ?? ""
                    : "";
            }
            catch
            {
                return false;
            }

            if (!enabled || !DisguiseHelper.IsValidProcessName(disguisedName))
            {
                return false;
            }

            var selfPath = FrameworkCompat.ProcessPath;
            var dir = string.IsNullOrEmpty(selfPath) ? null : Path.GetDirectoryName(selfPath);
            if (string.IsNullOrEmpty(dir))
            {
                return false;
            }

            // 已是伪装副本：不再重启，避免无限循环。
            if (string.Equals(Path.GetFileNameWithoutExtension(selfPath), disguisedName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var target = Path.Combine(dir, disguisedName + ".exe");
            if (string.Equals(target, selfPath, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            File.Copy(selfPath, target, overwrite: true);
            // 重命名后的 exe 需要同名的 .config（程序集绑定重定向），否则 CLR 会因缺少配置而崩溃。
            var configSource = selfPath + ".config";
            if (File.Exists(configSource))
            {
                File.Copy(configSource, target + ".config", overwrite: true);
            }

            Process.Start(new ProcessStartInfo(target)
            {
                Arguments = FrameworkCompat.JoinArguments(args),
                WorkingDirectory = dir
            });
            return true;
        }
        catch
        {
            // 复制/启动失败：按原进程继续正常启动。
            return false;
        }
    }

    private static async Task MainAsync(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += DiagnosticService.ProcessDomainUnhandledException;
        AppBase.CurrentLifetime = ApplicationLifetime.EarlyLoading;
        ConfigureFileHelper.SerializerOptions.Converters.Add(new ColorHexJsonConverter());
        ConfigureFileHelper.SerializerOptions.Converters.Add(new GuidEmptyFallbackConverter());

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

        var mutex = new Mutex(true, "LegacyIsland.Lock", out var createNew);
        var legacyClassIslandMutex = new Mutex(true, "ClassIsland.Lock", out var legacyClassIslandCreateNew);
        createNew = createNew && legacyClassIslandCreateNew;

        if (!createNew)
        {
            if (App.ApplicationCommand.WaitMutex)
            {
                try
                {
                    mutex?.WaitOne();
                    legacyClassIslandMutex?.WaitOne();
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

        if (Environment.GetEnvironmentVariable("LegacyIsland_ProcessPriority") is string priorityStr && uint.TryParse(priorityStr, out uint priority))
        {
            SetProcessPriority(priority);
        }
        else SetProcessPriority(2); //If not set or invalid, default to Normal priority (2).


        var app = new App()
        {
            Mutex = mutex,
            LegacyMutex = legacyClassIslandMutex,
            IsMutexCreateNew = createNew
        };
        app.InitializeComponent();
        app.Run();
        return;

        static void ProcessUriNavigation()
        {
            try
            {
                using var client = new NamedPipeClientStream(".", "LegacyIsland.Uri", PipeDirection.Out);
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
