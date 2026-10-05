using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Security.Principal;
using System.Text;

namespace ClassIsland.Helpers;

/// <summary>通过计划任务实现「开机以管理员身份启动」。创建/删除需要提权（会弹一次 UAC）。</summary>
public static class ScheduledTaskHelper
{
    public const string TaskName = "LegacyIsland";

    public static bool IsRegistered()
    {
        try
        {
            var startInfo = new ProcessStartInfo("schtasks.exe", $"/query /tn \"{TaskName}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var process = Process.Start(startInfo);
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

    public static bool Register(string exePath, string arguments)
    {
        var xmlPath = Path.Combine(Path.GetTempPath(), "LegacyIsland-task-" + Guid.NewGuid().ToString("N") + ".xml");
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
                if (File.Exists(xmlPath)) File.Delete(xmlPath);
            }
            catch
            {
                // ignored
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

    private static bool RunElevatedSchtasks(string arguments)
    {
        try
        {
            var startInfo = new ProcessStartInfo("schtasks.exe", arguments)
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using var process = Process.Start(startInfo);
            if (process == null)
            {
                return false;
            }
            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch (Win32Exception)
        {
            // 用户取消了 UAC 提权。
            return false;
        }
        catch
        {
            return false;
        }
    }

    private static string BuildTaskXml(string exePath, string arguments)
    {
        var sid = WindowsIdentity.GetCurrent().User?.Value ?? "";
        var command = SecurityElement.Escape(exePath) ?? "";
        var args = SecurityElement.Escape(arguments) ?? "";
        var user = SecurityElement.Escape(sid) ?? "";
        return $@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo>
    <Description>LegacyIsland 开机以管理员身份启动</Description>
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
}
