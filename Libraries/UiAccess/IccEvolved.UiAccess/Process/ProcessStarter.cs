using System;
using System.Runtime.InteropServices;
using System.Text;

namespace IccEvolved.UiAccess.Process;

/// <summary>
///     用打过标的主令牌创建目标进程。
///     winlogon/current 路径：CreateProcessWithTokenW（LOGON_WITH_PROFILE）。
///     服务路径：CreateProcessAsUserW（环境块 + 显式桌面 winsta0\default）。
///     注（2026-08-11 实验）：曾把两条路径统一改为 CreateProcessAsUserW 以让 UIAccess 标被窗口管理器
///     认可（实测 Z 序超级置顶有效）；因用户侧反馈 Demo 异常，本次回退为原 CreateProcessWithTokenW
///     实现。被注释的统一 AsUser 版本保留在文件末尾，需要时启用。
/// </summary>
internal static class ProcessStarter
{
    /// <summary>成功返回 true 并输出 pid；失败输出错误码。</summary>
    public static bool Create(SafeKernelHandle primaryToken, string fullCommandLine,
        bool fromService, bool loadProfile, bool newConsole, uint extraFlags, string desktop,
        out int pid, out int error)
    {
        pid = 0;
        error = 0;
        var flags = extraFlags | (newConsole ? NativeMethods.CREATE_NEW_CONSOLE : 0);

        var cmd = new StringBuilder(fullCommandLine);
        var si = new NativeMethods.STARTUPINFO
        {
            cb = Marshal.SizeOf(typeof(NativeMethods.STARTUPINFO))
        };
        var env = IntPtr.Zero;

        try
        {
            if (fromService)
            {
                // 服务进程在非交互窗口站，必须显式指定桌面，否则目标窗口不显示
                si.lpDesktop = string.IsNullOrEmpty(desktop) ? "winsta0\\default" : desktop;
                if (loadProfile)
                {
                    IntPtr envBlock;
                    if (NativeMethods.CreateEnvironmentBlock(out envBlock, primaryToken.DangerousGetHandle(), true))
                    {
                        env = envBlock;
                        flags |= NativeMethods.CREATE_UNICODE_ENVIRONMENT;
                    }
                }

                NativeMethods.PROCESS_INFORMATION pi;
                if (!NativeMethods.CreateProcessAsUserW(primaryToken.DangerousGetHandle(), null, cmd,
                        IntPtr.Zero, IntPtr.Zero, false, flags, env, null, ref si, out pi))
                {
                    error = Win32.LastError;
                    return false;
                }

                pid = unchecked((int)pi.dwProcessId);
                NativeMethods.CloseHandle(pi.hThread);
                NativeMethods.CloseHandle(pi.hProcess);
                return true;
            }
            else
            {
                si.lpDesktop = string.IsNullOrEmpty(desktop) ? null : desktop;
                var logonFlags = loadProfile ? NativeMethods.LOGON_WITH_PROFILE : 0;

                NativeMethods.PROCESS_INFORMATION pi;
                if (!NativeMethods.CreateProcessWithTokenW(primaryToken.DangerousGetHandle(), logonFlags,
                        null, cmd, flags, IntPtr.Zero, null, ref si, out pi))
                {
                    error = Win32.LastError;
                    return false;
                }

                pid = unchecked((int)pi.dwProcessId);
                NativeMethods.CloseHandle(pi.hThread);
                NativeMethods.CloseHandle(pi.hProcess);
                return true;
            }
        }
        finally
        {
            if (env != IntPtr.Zero)
                NativeMethods.DestroyEnvironmentBlock(env);
        }
    }
}

/*
// =====================================================================================
// 统一 CreateProcessAsUserW 版本（2026-08-11 实验，用户侧反馈异常后回退，勿删）。
//
// 背景：CreateProcessWithTokenW 创建的进程 UIAccess 标不被窗口管理器认可（标在令牌上但
// 超级置顶/更高 IL 访问失效）。winlogon 路径调用者已模拟 winlogon（SYSTEM，具备
// SeAssignPrimaryToken/SeIncreaseQuota），服务路径本就 AsUser → 统一 AsUser。
// 环境块手动创建（AsUser 不自动加载 profile）；桌面：服务路径显式 winsta0\default，其余继承调用方。
//
// internal static class ProcessStarter
// {
//     public static bool Create(SafeKernelHandle primaryToken, string fullCommandLine,
//         bool fromService, bool loadProfile, bool newConsole, uint extraFlags, string desktop,
//         out int pid, out int error)
//     {
//         pid = 0;
//         error = 0;
//         uint flags = extraFlags | (newConsole ? NativeMethods.CREATE_NEW_CONSOLE : 0);
//
//         var cmd = new StringBuilder(fullCommandLine);
//         var si = new NativeMethods.STARTUPINFO
//         {
//             cb = Marshal.SizeOf(typeof(NativeMethods.STARTUPINFO))
//         };
//         IntPtr env = IntPtr.Zero;
//
//         try
//         {
//             if (fromService)
//             {
//                 // 服务进程在非交互窗口站，必须显式指定桌面，否则目标窗口不显示
//                 si.lpDesktop = string.IsNullOrEmpty(desktop) ? "winsta0\\default" : desktop;
//             }
//             else
//             {
//                 // winlogon/current 路径：继承调用方桌面
//                 si.lpDesktop = desktop;
//             }
//             if (loadProfile)
//             {
//                 IntPtr envBlock;
//                 if (NativeMethods.CreateEnvironmentBlock(out envBlock, primaryToken.DangerousGetHandle(), true))
//                 {
//                     env = envBlock;
//                     flags |= NativeMethods.CREATE_UNICODE_ENVIRONMENT;
//                 }
//             }
//
//             NativeMethods.PROCESS_INFORMATION pi;
//             if (!NativeMethods.CreateProcessAsUserW(primaryToken.DangerousGetHandle(), null, cmd,
//                 IntPtr.Zero, IntPtr.Zero, false, flags, env, null, ref si, out pi))
//             {
//                 error = Win32.LastError;
//                 return false;
//             }
//             pid = unchecked((int)pi.dwProcessId);
//             NativeMethods.CloseHandle(pi.hThread);
//             NativeMethods.CloseHandle(pi.hProcess);
//             return true;
//         }
//         finally
//         {
//             if (env != IntPtr.Zero)
//                 NativeMethods.DestroyEnvironmentBlock(env);
//         }
//     }
// }
// =====================================================================================
*/