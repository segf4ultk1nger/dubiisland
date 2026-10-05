using System;
using System.Threading;
using IccEvolved.UiAccess.Elevator;
using IccEvolved.UiAccess.Ipc;

namespace IccEvolved.UiAccess.Tokens;

/// <summary>
///     一次性服务后端（对应 RunUIAccess 的做法）：
///     建一个 SERVICE_DEMAND_START 服务，服务二进制 = IccEvolved.UiAccess.Helper.exe --elevator &lt;svcName&gt; &lt;ipcName&gt;，
///     服务进程以 SYSTEM 运行，执行 ElevatorJob 后自删。
/// </summary>
internal static class ServiceIdentity
{
    private const int ServiceTimeoutMs = 10000;

    /// <summary>通过一次性服务执行提权作业。helperExePath 用于服务二进制路径。</summary>
    public static ElevatorJobResult RunViaService(ElevatorJobArgs a, string helperExePath, out int error)
    {
        error = 0;
        var result = new ElevatorJobResult();
        if (string.IsNullOrEmpty(helperExePath))
        {
            error = 87; // ERROR_INVALID_PARAMETER
            return result;
        }

        var ipcName = "IccEvolved.UiAccess." + Guid.NewGuid().ToString("N");
        var svcName = "IccUiAccessElevator" + Guid.NewGuid().ToString("N").Substring(0, 12);

        using (var ipc = NamedFileMap.Create(ipcName))
        {
            if (ipc == null)
            {
                error = Win32.LastError;
                return result;
            }

            ipc.Write(ElevatorIpc.FromJobArgs(a));

            using (var scm = new SafeServiceHandle(
                       NativeMethods.OpenSCManagerW(null, null, NativeMethods.SC_MANAGER_ALL_ACCESS)))
            {
                if (scm.IsInvalid)
                {
                    error = Win32.LastError;
                    return result;
                }

                var binPath = "\"" + helperExePath + "\" --elevator " + svcName + " " + ipcName;
                var hSvc = NativeMethods.CreateServiceW(scm.DangerousGetHandle(), svcName,
                    "Icc UiAccess Elevator (one-shot)",
                    NativeMethods.SERVICE_ALL_ACCESS, NativeMethods.SERVICE_WIN32_OWN_PROCESS,
                    NativeMethods.SERVICE_DEMAND_START, NativeMethods.SERVICE_ERROR_IGNORE,
                    binPath, null, IntPtr.Zero, null, null, null);
                if (hSvc == IntPtr.Zero)
                {
                    error = Win32.LastError;
                    return result;
                }

                using (var svc = new SafeServiceHandle(hSvc))
                {
                    if (!NativeMethods.StartServiceW(svc.DangerousGetHandle(), 0, null))
                    {
                        error = Win32.LastError;
                        return result;
                    }

                    // 轮询结果
                    var deadline = Environment.TickCount + ServiceTimeoutMs;
                    while (Environment.TickCount < deadline)
                    {
                        var ipc2 = ipc.Read();
                        if (ipc2.Magic != NativeMethods.ElevatorIpcMagic)
                        {
                            Thread.Sleep(100);
                            continue;
                        }

                        if (ipc2.Status == NativeMethods.ElevatorStatusSuccess)
                        {
                            result.Success = true;
                            result.ChildPid = ipc2.ChildPid;
                            return result;
                        }

                        if (ipc2.Status == NativeMethods.ElevatorStatusFailed)
                        {
                            error = ipc2.Error;
                            return result;
                        }

                        Thread.Sleep(100);
                    }

                    error = NativeMethods.ERROR_TIMEOUT; // 1460
                    return result;
                }
            }
        }
    }

    /// <summary>清理服务（helper 退出时兜底自删）。</summary>
    public static void DeleteServiceByName(string svcName)
    {
        if (string.IsNullOrEmpty(svcName)) return;
        using (var scm = new SafeServiceHandle(
                   NativeMethods.OpenSCManagerW(null, null, NativeMethods.SC_MANAGER_ALL_ACCESS)))
        {
            if (scm.IsInvalid) return;
            var hSvc = NativeMethods.OpenServiceW(scm.DangerousGetHandle(), svcName, NativeMethods.SERVICE_ALL_ACCESS);
            if (hSvc == IntPtr.Zero) return;
            using (var svc = new SafeServiceHandle(hSvc))
            {
                NativeMethods.DeleteService(svc.DangerousGetHandle());
            }
        }
    }
}