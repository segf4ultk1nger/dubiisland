using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using ClassIsland.Core;

namespace Org.Sifware.UiAccessX;

/// <summary>
/// 「以管理员身份开机启动」（计划任务）与「以管理员身份重启」。
/// </summary>
internal static class AdminStartup
{
    public const string TaskName = "ClassIsland2-UIAccessX";

    public static bool IsRegistered()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("schtasks.exe", $"/query /tn \"{TaskName}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            if (process == null)
            {
                return false;
            }

            process.WaitForExit(10000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>注册/取消注册「以管理员身份开机启动」的计划任务。返回是否成功。</summary>
    public static bool SetEnabled(bool enabled)
    {
        if (enabled)
        {
            // 优先用 CI 的稳定入口（安装包根目录的启动器），它不会随版本更新改变路径。
            var exe = AppBase.ExecutingEntrance;
            if (string.IsNullOrEmpty(exe))
            {
                exe = Environment.ProcessPath;
            }

            if (string.IsNullOrEmpty(exe))
            {
                return false;
            }

            return Register(exe, "-au");
        }

        return Unregister();
    }

    public static bool Register(string exePath, string arguments)
    {
        var xmlPath = Path.Combine(Path.GetTempPath(), $"ClassIsland2-UIAccessX-{Guid.NewGuid():N}.xml");
        try
        {
            File.WriteAllText(xmlPath, BuildTaskXml(exePath, arguments), Encoding.Unicode);
            return RunElevatedSchtasks($"/create /tn \"{TaskName}\" /xml \"{xmlPath}\" /f");
        }
        catch
        {
            return false;
        }
        finally
        {
            try
            {
                if (File.Exists(xmlPath))
                {
                    File.Delete(xmlPath);
                }
            }
            catch
            {
                // 忽略。
            }
        }
    }

    public static bool Unregister()
    {
        if (!IsRegistered())
        {
            return true;
        }

        return RunElevatedSchtasks($"/delete /tn \"{TaskName}\" /f");
    }

    /// <summary>以管理员身份重启当前应用。</summary>
    public static void RestartAsAdmin()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe))
        {
            return;
        }

        try
        {
            var args = Environment.GetCommandLineArgs().Skip(1)
                .Where(a => a is not ("-m" or "--waitMutex"))
                .ToList();
            args.Add("-m");
            args.Add("--uri");
            args.Add("classisland://app/settings/general");

            Process.Start(new ProcessStartInfo(exe)
            {
                UseShellExecute = true,
                Verb = "runas",
                Arguments = string.Join(' ', args.Select(Quote))
            });
        }
        catch (Win32Exception)
        {
            // 用户取消了 UAC。
            return;
        }

        AppBase.Current.Stop();
    }

    private static string Quote(string value) =>
        value.Contains(' ') || value.Contains('\t')
            ? "\"" + value.Replace("\"", "\\\"") + "\""
            : value;

    private static bool RunElevatedSchtasks(string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("schtasks.exe", arguments)
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            });
            if (process == null)
            {
                return false;
            }

            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch (Win32Exception)
        {
            // 用户取消了 UAC。
            return false;
        }
        catch
        {
            return false;
        }
    }

    private static string BuildTaskXml(string exePath, string arguments)
    {
        var user = XmlEscape($"{Environment.UserDomainName}\\{Environment.UserName}");
        var command = XmlEscape(exePath);
        var args = XmlEscape(arguments);
        return $@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo>
    <Description>ClassIsland2 UIAccessX 开机以管理员身份启动</Description>
  </RegistrationInfo>
  <Triggers>
    <LogonTrigger>
      <Enabled>true</Enabled>
      <UserId>{user}</UserId>
    </LogonTrigger>
  </Triggers>
  <Principals>
    <Principal id=""Author"">
      <UserId>{user}</UserId>
      <LogonType>InteractiveToken</LogonType>
      <RunLevel>HighestAvailable</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>false</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <IdleSettings>
      <StopOnIdleEnd>true</StopOnIdleEnd>
      <RestartOnIdle>false</RestartOnIdle>
    </IdleSettings>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>false</Hidden>
    <RunOnlyIfIdle>false</RunOnlyIfIdle>
    <WakeToRun>false</WakeToRun>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Priority>7</Priority>
  </Settings>
  <Actions Context=""Author"">
    <Exec>
      <Command>{command}</Command>
      <Arguments>{args}</Arguments>
    </Exec>
  </Actions>
</Task>";
    }

    private static string XmlEscape(string value) => value
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;")
        .Replace("\"", "&quot;")
        .Replace("'", "&apos;");
}
