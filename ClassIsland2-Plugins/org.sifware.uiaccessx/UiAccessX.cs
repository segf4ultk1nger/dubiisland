using System.Text.Json;
using ClassIsland.Core;
using IccEvolved.UiAccess;

namespace Org.Sifware.UiAccessX;

/// <summary>
/// 插件运行时：设置读写、UIAccess 自重启、任务栏修复挂载。
/// </summary>
internal static class UiAccessX
{
    private const string SettingsFileName = "settings.json";
    private const string RelaunchFlagName = "uiaccess-relaunch.flag";
    private const int RelaunchGuardSeconds = 20;

    public static string ConfigFolder { get; private set; } = "";
    public static UiAccessXSettings Settings { get; private set; } = new();

    private static string SettingsPath => Path.Combine(ConfigFolder, SettingsFileName);
    private static string RelaunchFlagPath => Path.Combine(ConfigFolder, RelaunchFlagName);

    /// <summary>插件自身所在目录（注意不是宿主 ClassIsland 的应用目录）。</summary>
    private static string PluginDirectory =>
        Path.GetDirectoryName(typeof(UiAccessX).Assembly.Location) ?? AppContext.BaseDirectory;

    /// <summary>Helper 随插件发布，位于插件目录的 UiAccessHelper 子目录。</summary>
    private static string HelperDirectory => Path.Combine(PluginDirectory, "UiAccessHelper");

    public static string HelperExePath =>
        Path.Combine(HelperDirectory, "IccEvolved.UiAccess.Helper.exe");

    public static void Initialize(string configFolder)
    {
        ConfigFolder = string.IsNullOrWhiteSpace(configFolder)
            ? Path.Combine(AppContext.BaseDirectory, "uiaccessx")
            : configFolder;

        try
        {
            Directory.CreateDirectory(ConfigFolder);
        }
        catch
        {
            // 目录不可写时依旧继续，设置将回退为默认值。
        }

        Settings = LoadSettings();

        if (OperatingSystem.IsWindows())
        {
            try
            {
                AppBase.Current.AppStarted += (_, _) =>
                {
                    if (OperatingSystem.IsWindows())
                    {
                        TaskbarFixer.Start();
                    }
                };
            }
            catch
            {
                // AppBase.Current 在极早期可能不可用，忽略。
            }
        }
    }

    public static void SaveSettings()
    {
        try
        {
            File.WriteAllText(SettingsPath,
                JsonSerializer.Serialize(Settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // 忽略写入失败。
        }
    }

    private static UiAccessXSettings LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                return JsonSerializer.Deserialize<UiAccessXSettings>(File.ReadAllText(SettingsPath))
                       ?? new UiAccessXSettings();
            }
        }
        catch
        {
            // 配置损坏时回退默认值。
        }

        return new UiAccessXSettings();
    }

    public static bool IsUiAccessRunning() =>
        OperatingSystem.IsWindows() && UiAccessLauncher.IsUiAccess();

    public static bool IsElevated() =>
        OperatingSystem.IsWindows() && UiAccessLauncher.IsElevated();

    public static bool IsHelperAvailable()
    {
        try
        {
            return File.Exists(HelperExePath)
                   && File.Exists(Path.Combine(HelperDirectory, "IccEvolved.UiAccess.dll"));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 若已启用且当前进程尚无 UIAccess，则通过 Helper 以 UIAccess 身份重启自身。
    /// 成功启动接管进程后本进程退出。
    /// </summary>
    public static void TryRelaunchWithUiAccess()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            if (UiAccessLauncher.IsUiAccess())
            {
                // 已经成为 UIAccess 子进程，清理守卫标记。
                TryDelete(RelaunchFlagPath);
                return;
            }
        }
        catch
        {
            return;
        }

        if (!Settings.EnableUiAccess || !IsHelperAvailable())
        {
            return;
        }

        // 提权失败（如未签名）时避免无限重启：短时间内刚尝试过就跳过这一次。
        try
        {
            if (File.Exists(RelaunchFlagPath) &&
                DateTime.UtcNow - File.GetLastWriteTimeUtc(RelaunchFlagPath) < TimeSpan.FromSeconds(RelaunchGuardSeconds))
            {
                return;
            }
        }
        catch
        {
            // 忽略。
        }

        try
        {
            File.WriteAllText(RelaunchFlagPath, DateTime.UtcNow.Ticks.ToString());
        }
        catch
        {
            // 忽略。
        }

        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe))
        {
            return;
        }

        var options = new UiAccessOptions
        {
            Enabled = true,
            LaunchMode = LaunchMode.LaunchTarget,
            TargetExe = exe,
            TargetCmdLine = BuildTargetCmdLine(),
            UiAccess = true,
            HelperExePath = HelperExePath,
            TargetTokenMode = TargetTokenMode.SelfPid
        };
        options.SystemIdentityChain.Clear();
        options.SystemIdentityChain.Add(SystemIdentityMethod.Service);
        options.SystemIdentityChain.Add(SystemIdentityMethod.Winlogon);

        UiAccessResult result;
        try
        {
            result = UiAccessLauncher.Launch(options);
        }
        catch
        {
            return;
        }

        if (result.Status == UiAccessStatus.Succeeded)
        {
            Environment.Exit(0);
        }
    }

    /// <summary>把当前命令行参数传递给子进程，并补上 -m 让子进程等待旧实例释放互斥体。</summary>
    private static string BuildTargetCmdLine()
    {
        var args = Environment.GetCommandLineArgs().Skip(1).ToList();
        args = args.Where(a => !a.StartsWith("--uiaccess", StringComparison.OrdinalIgnoreCase)).ToList();

        if (args.All(a => a is not ("-m" or "--waitMutex")))
        {
            args.Add("-m");
        }

        return string.Join(' ', args.Select(Quote));
    }

    private static string Quote(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "\"\"";
        }

        return value.Contains(' ') || value.Contains('\t')
            ? "\"" + value.Replace("\"", "\\\"") + "\""
            : value;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // 忽略。
        }
    }
}
