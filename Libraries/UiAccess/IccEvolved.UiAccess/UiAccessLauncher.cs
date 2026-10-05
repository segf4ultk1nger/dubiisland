using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using IccEvolved.UiAccess.Elevator;
using IccEvolved.UiAccess.Process;
using IccEvolved.UiAccess.Tokens;

namespace IccEvolved.UiAccess;

/// <summary>UIAccess 启动器的门面。应用侧调用 <see cref="Launch" />，提权上下文调用 <see cref="RunElevatedJob" />。</summary>
public static class UiAccessLauncher
{
    /// <summary>当前进程是否已具备 UIAccess。</summary>
    public static bool IsUiAccess()
    {
        using (var tok = TokenUtil.OpenSelfToken())
        {
            return tok != null && !tok.IsInvalid && TokenUtil.IsUiAccess(tok);
        }
    }

    /// <summary>当前进程是否以管理员（提升）权限运行。</summary>
    public static bool IsElevated()
    {
        using (var tok = TokenUtil.OpenSelfToken())
        {
            return tok != null && !tok.IsInvalid && TokenUtil.IsElevated(tok);
        }
    }

    /// <summary>当前进程是否拥有 SeTcbPrivilege（SYSTEM 上下文）。</summary>
    public static bool HasSeTcbPrivilege()
    {
        return TokenUtil.CurrentProcessHasSeTcb();
    }

    /// <summary>
    ///     应用侧完整流程：
    ///     已提权 → 进程内直接执行；未提权 → 用 helper 以 runas 提权后执行。
    ///     relaunchSelf 模式下成功后旧进程应自行退出（交接文件已出现）。
    /// </summary>
    public static UiAccessResult Launch(UiAccessOptions options)
    {
        if (options == null || !options.Enabled)
            return new UiAccessResult(UiAccessStatus.NotEnabled);
        if (IsUiAccess())
            return new UiAccessResult(UiAccessStatus.AlreadyUiAccess);

        var verr = options.Validate();
        if (verr != null)
            return UiAccessResult.Fail(UiAccessStatus.InvalidConfig, 0, verr);
        PrepareOptions(options);

        if (IsElevated())
            return RunElevatedJob(options);

        // 未提权：把作业写文件，用 helper runas 提权执行
        var helper = ResolveHelperPath(options);
        if (!File.Exists(helper))
            return UiAccessResult.Fail(UiAccessStatus.ElevationFailed, 0, "找不到 helper: " + helper);

        string jobPath = null;
        try
        {
            jobPath = JobFile.Write(options);

            var psi = new ProcessStartInfo
            {
                FileName = helper,
                Arguments = "--job \"" + jobPath + "\"",
                Verb = "runas",
                UseShellExecute = true
            };
            using (var p = System.Diagnostics.Process.Start(psi))
            {
                if (!p.WaitForExit(60000))
                {
                    try
                    {
                        p.Kill();
                    }
                    catch
                    {
                    }

                    return UiAccessResult.Fail(UiAccessStatus.TimedOut, 0, "helper 运行超时");
                }

                var res = JobFile.ReadResult(jobPath);
                if (res == null)
                    res = p.ExitCode == 0
                        ? new UiAccessResult(UiAccessStatus.Succeeded)
                        : UiAccessResult.Fail(UiAccessStatus.Failed, p.ExitCode, "helper 退出码 " + p.ExitCode);

                if (res.Status == UiAccessStatus.Succeeded && options.LaunchMode == LaunchMode.RelaunchSelf)
                    if (!SelfRelauncher.WaitForHandoff(options.HandoffSignal, options.WaitOldExitTimeoutMs))
                        return UiAccessResult.Fail(UiAccessStatus.Failed, 0, "交接文件未出现");
                return res;
            }
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == NativeMethods.ERROR_CANCELLED)
        {
            return new UiAccessResult(UiAccessStatus.CancelledByUser);
        }
        catch (Win32Exception ex)
        {
            return UiAccessResult.Fail(UiAccessStatus.ElevationFailed, ex.NativeErrorCode, ex.Message);
        }
        catch (Exception ex)
        {
            return UiAccessResult.Fail(UiAccessStatus.ElevationFailed, 0, ex.Message);
        }
        finally
        {
            try
            {
                if (jobPath != null) File.Delete(jobPath);
            }
            catch
            {
            }
        }
    }

    /// <summary>
    ///     提权上下文内的作业执行（helper --job 或已提权应用进程内调用）。
    ///     按 SystemIdentityChain 顺序尝试：CurrentIfSystem → Winlogon → Service。
    /// </summary>
    public static UiAccessResult RunElevatedJob(UiAccessOptions options)
    {
        if (options == null || !options.Enabled)
            return new UiAccessResult(UiAccessStatus.NotEnabled);
        if (IsUiAccess())
            return new UiAccessResult(UiAccessStatus.AlreadyUiAccess);

        var verr = options.Validate();
        if (verr != null)
            return UiAccessResult.Fail(UiAccessStatus.InvalidConfig, 0, verr);
        PrepareOptions(options);

        var a = new ElevatorJobArgs
        {
            TargetMode = options.TargetTokenMode,
            TargetPid = options.TargetPid ?? System.Diagnostics.Process.GetCurrentProcess().Id,
            SessionId = options.SessionId ??
                        GetSessionOfPid(options.TargetPid ?? System.Diagnostics.Process.GetCurrentProcess().Id),
            UiAccess = options.UiAccess,
            LoadProfile = options.LoadProfile,
            NewConsole = options.NewConsole,
            ExtraCreateFlags = options.ExtraCreateFlags,
            EnableAllPrivilegesOnTarget = options.EnableAllPrivilegesOnTarget,
            TargetExe = options.TargetExe,
            Desktop = options.Desktop,
            FullCommandLine = SelfRelauncher.BuildFullCommandLine(
                options.TargetExe, options.TargetCmdLine, options.MarkerArg,
                options.HandoffSignal, options.LaunchMode == LaunchMode.RelaunchSelf)
        };

        var lastError = 0;
        var errors = new List<string>();
        foreach (var method in options.SystemIdentityChain)
        {
            ElevatorJobResult r = null;
            try
            {
                switch (method)
                {
                    case SystemIdentityMethod.CurrentIfSystem:
                        if (!TokenUtil.CurrentProcessHasSeTcb())
                        {
                            errors.Add("当前无 SeTcbPrivilege");
                            continue;
                        }

                        a.FromService = false;
                        r = ElevatorJob.Execute(a);
                        break;

                    case SystemIdentityMethod.Winlogon:
                        int werr;
                        using (var identity = WinlogonSystemIdentity.TryImpersonate(out werr))
                        {
                            if (identity == null)
                            {
                                errors.Add("winlogon 令牌失败 (Win32 " + werr + ")");
                                continue;
                            }

                            a.FromService = false;
                            r = ElevatorJob.Execute(a);
                        }

                        break;

                    case SystemIdentityMethod.Service:
                        int serr;
                        r = ServiceIdentity.RunViaService(a, ResolveHelperPath(options), out serr);
                        if (r == null || !r.Success)
                            errors.Add("服务后端失败 (Win32 " + serr + ")");
                        break;
                }
            }
            catch (Exception ex)
            {
                errors.Add(method + ": " + ex.Message);
                continue;
            }

            if (r != null && r.Success)
            {
                if (options.LaunchMode == LaunchMode.RelaunchSelf)
                    SelfRelauncher.WriteHandoff(options.HandoffSignal);
                return new UiAccessResult(UiAccessStatus.Succeeded, childPid: r.ChildPid) { UsedMethod = method };
            }

            lastError = r != null ? r.Error : 0;
        }

        return UiAccessResult.Fail(UiAccessStatus.Failed, lastError, string.Join("; ", errors));
    }

    // ---- 内部 ----

    private static void PrepareOptions(UiAccessOptions options)
    {
        if (options.TargetPid == null)
            options.TargetPid = System.Diagnostics.Process.GetCurrentProcess().Id;
        if (options.SessionId == null)
            options.SessionId = GetSessionOfPid(options.TargetPid.Value);
        if (string.IsNullOrEmpty(options.TargetExe))
        {
            var exe = Environment.GetCommandLineArgs()[0];
            try
            {
                options.TargetExe = Path.GetFullPath(exe);
            }
            catch
            {
                options.TargetExe = exe;
            }
        }

        if (options.TargetCmdLine == null)
            options.TargetCmdLine = ReconstructArgs(Environment.GetCommandLineArgs());
        if (string.IsNullOrEmpty(options.HelperExePath))
            options.HelperExePath =
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "IccEvolved.UiAccess.Helper.exe");
    }

    internal static string ResolveHelperPath(UiAccessOptions options)
    {
        if (!string.IsNullOrEmpty(options.HelperExePath) && File.Exists(options.HelperExePath))
            return options.HelperExePath;
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var candidate = Path.Combine(baseDir, "IccEvolved.UiAccess.Helper.exe");
        if (File.Exists(candidate)) return candidate;
        return options.HelperExePath ?? candidate;
    }

    private static int GetSessionOfPid(int pid)
    {
        uint session;
        if (NativeMethods.ProcessIdToSessionId((uint)pid, out session))
            return unchecked((int)session);
        return 0;
    }

    /// <summary>把命令行参数（不含 exe）重组为字符串。</summary>
    private static string ReconstructArgs(string[] args)
    {
        var sb = new StringBuilder();
        for (var i = 1; i < args.Length; i++)
        {
            if (i > 1) sb.Append(' ');
            sb.Append(SelfRelauncher.Quote(args[i]));
        }

        return sb.ToString();
    }
}