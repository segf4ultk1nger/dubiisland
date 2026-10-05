using System;
using System.Threading;
using IccEvolved.UiAccess.Elevator;
using IccEvolved.UiAccess.Ipc;
using IccEvolved.UiAccess.Tokens;

namespace IccEvolved.UiAccess.Helper;

/// <summary>
///     提权中间人。
///     --job &lt;作业文件&gt;    以 runas 提权运行，执行 RunElevatedJob 并写回结果文件。
///     --elevator &lt;服务名&gt; &lt;ipc名&gt;  作为一次性服务（SYSTEM）运行，执行 ElevatorJob 并自删服务。
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args == null || args.Length == 0)
            return 87; // ERROR_INVALID_PARAMETER

        try
        {
            if (string.Equals(args[0], "--job", StringComparison.OrdinalIgnoreCase))
            {
                if (args.Length < 2) return 87;
                return RunJob(args[1]);
            }

            if (string.Equals(args[0], "--elevator", StringComparison.OrdinalIgnoreCase))
            {
                if (args.Length < 3) return 87;
                return RunElevatorService(args[1], args[2]);
            }

            if (string.Equals(args[0], "--selfcheck", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("elevated=" + (UiAccessLauncher.IsElevated() ? "1" : "0"));
                Console.WriteLine("uiaccess=" + (UiAccessLauncher.IsUiAccess() ? "1" : "0"));
                Console.WriteLine("setcb=" + (UiAccessLauncher.HasSeTcbPrivilege() ? "1" : "0"));
                return 0;
            }

            return 87;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    /// <summary>--job：读取作业文件，进程内执行提权作业（此时本进程已被 runas 提权）。</summary>
    private static int RunJob(string jobPath)
    {
        UiAccessOptions options;
        try
        {
            options = JobFile.Read(jobPath);
        }
        catch (Exception ex)
        {
            return Fail(jobPath, new UiAccessResult(UiAccessStatus.ElevationFailed, 0, "读取作业文件失败: " + ex.Message));
        }

        var result = UiAccessLauncher.RunElevatedJob(options);
        try
        {
            JobFile.WriteResult(jobPath, result);
        }
        catch
        {
            /* 结果文件写失败不影响作业本身 */
        }

        // 静默退出码：0 = 成功/已具备，非 0 = 失败
        return result.IsSuccess ? 0 : 1;
    }

    private static int Fail(string jobPath, UiAccessResult result)
    {
        try
        {
            JobFile.WriteResult(jobPath, result);
        }
        catch
        {
        }

        return 1;
    }

    /// <summary>--elevator：作为一次性服务（SYSTEM）执行作业。</summary>
    private static int RunElevatorService(string svcName, string ipcName)
    {
        var table = new[]
        {
            new NativeMethods.SERVICE_TABLE_ENTRY
            {
                lpServiceName = svcName,
                lpServiceProc = (argc, argv) => ServiceMain(svcName, ipcName)
            },
            new NativeMethods.SERVICE_TABLE_ENTRY { lpServiceName = null, lpServiceProc = null }
        };

        if (!NativeMethods.StartServiceCtrlDispatcherW(table))
            return Win32.LastError;
        return 0;
    }

    private static void ServiceMain(string svcName, string ipcName)
    {
        var hStatus = IntPtr.Zero;
        NativeMethods.ServiceControlHandlerDelegate handler = control =>
        {
            if (control == 1) // SERVICE_CONTROL_STOP
            {
                var st = new NativeMethods.SERVICE_STATUS
                {
                    dwServiceType = NativeMethods.SERVICE_WIN32_OWN_PROCESS,
                    dwCurrentState = NativeMethods.SERVICE_STOPPED,
                    dwControlsAccepted = 0
                };
                NativeMethods.SetServiceStatus(hStatus, ref st);
            }
        };

        try
        {
            hStatus = NativeMethods.RegisterServiceCtrlHandlerW(svcName, handler);
            Report(hStatus, NativeMethods.SERVICE_START_PENDING);
            Report(hStatus, NativeMethods.SERVICE_RUNNING);

            // 执行作业
            ElevatorJobResult result = null;
            using (var ipc = NamedFileMap.Open(ipcName))
            {
                if (ipc == null)
                {
                    WriteFailure(ipcName, Win32.LastError);
                    return;
                }

                var req = ipc.Read();
                if (req.Magic != NativeMethods.ElevatorIpcMagic)
                {
                    WriteFailure(ipcName, 87);
                    return;
                }

                var a = new ElevatorJobArgs
                {
                    TargetMode = (TargetTokenMode)req.TargetTokenMode,
                    TargetPid = req.TargetPid,
                    SessionId = req.SessionId,
                    UiAccess = req.UiAccess != 0,
                    LoadProfile = req.LoadProfile != 0,
                    NewConsole = req.NewConsole != 0,
                    ExtraCreateFlags = unchecked((uint)req.ExtraCreateFlags),
                    EnableAllPrivilegesOnTarget = req.EnableAllPrivilegesOnTarget != 0,
                    TargetExe = req.TargetExe,
                    FullCommandLine = req.TargetCmdLine,
                    Desktop = req.Desktop,
                    FromService = true
                };
                result = ElevatorJob.Execute(a);

                var resp = req;
                resp.Status = result.Success ? NativeMethods.ElevatorStatusSuccess : NativeMethods.ElevatorStatusFailed;
                resp.Error = result.Error;
                resp.ChildPid = result.ChildPid;
                ipc.Write(resp);
            }

            Thread.Sleep(500); // 给中间人一点时间读到结果
            Report(hStatus, NativeMethods.SERVICE_STOPPED);
        }
        catch (Exception)
        {
            Report(hStatus, NativeMethods.SERVICE_STOPPED);
        }
        finally
        {
            // 自删服务（兜底清理）
            ServiceIdentity.DeleteServiceByName(svcName);
        }
    }

    private static void WriteFailure(string ipcName, int error)
    {
        try
        {
            using (var ipc = NamedFileMap.Open(ipcName))
            {
                if (ipc == null) return;
                var resp = ipc.Read();
                resp.Magic = NativeMethods.ElevatorIpcMagic;
                resp.Status = NativeMethods.ElevatorStatusFailed;
                resp.Error = error;
                ipc.Write(resp);
            }
        }
        catch
        {
        }
    }

    private static void Report(IntPtr hStatus, uint state)
    {
        if (hStatus == IntPtr.Zero) return;
        var st = new NativeMethods.SERVICE_STATUS
        {
            dwServiceType = NativeMethods.SERVICE_WIN32_OWN_PROCESS,
            dwCurrentState = state,
            dwControlsAccepted = state == NativeMethods.SERVICE_STOPPED ? 0 : NativeMethods.SERVICE_ACCEPT_STOP,
            dwWin32ExitCode = 0,
            dwCheckPoint = state == NativeMethods.SERVICE_RUNNING ? 0u : 1u,
            dwWaitHint = 0
        };
        NativeMethods.SetServiceStatus(hStatus, ref st);
    }
}