using System;
using System.Runtime.InteropServices;

namespace IccEvolved.UiAccess.Tokens;

/// <summary>
///     偷同会话 winlogon.exe 的令牌并模拟成 SYSTEM（线程级）。
///     在 using 块内调用 ElevatorJob.Execute 即处于 SeTcbPrivilege 上下文。
///     融合了 C++ 版的"同会话匹配"与 killtimer0 版的"PrivilegeCheck(SeTcb)"双重校验。
/// </summary>
internal sealed class WinlogonSystemIdentity : IDisposable
{
    private readonly SafeKernelHandle _impersonationToken;
    private bool _reverted;

    private WinlogonSystemIdentity(SafeKernelHandle impersonationToken)
    {
        _impersonationToken = impersonationToken;
    }

    public void Dispose()
    {
        if (!_reverted)
        {
            _reverted = true;
            NativeMethods.RevertToSelf();
        }

        _impersonationToken?.Dispose();
    }

    /// <summary>尝试在会话内找到 winlogon 并模拟。失败返回 null 并给出错误码。</summary>
    public static WinlogonSystemIdentity TryImpersonate(out int error)
    {
        error = 0;
        using (var self = TokenUtil.OpenSelfToken())
        {
            if (self == null || self.IsInvalid)
            {
                error = Win32.LastError;
                return null;
            }

            var selfSession = TokenUtil.GetSessionId(self);

            using (var snapshot = new SafeKernelHandle(
                       NativeMethods.CreateToolhelp32Snapshot(NativeMethods.TH32CS_SNAPPROCESS, 0)))
            {
                if (snapshot.IsInvalid)
                {
                    error = Win32.LastError;
                    return null;
                }

                var pe = new NativeMethods.PROCESSENTRY32W
                {
                    dwSize = (uint)Marshal.SizeOf(typeof(NativeMethods.PROCESSENTRY32W))
                };

                for (var cont = NativeMethods.Process32FirstW(snapshot.DangerousGetHandle(), ref pe);
                     cont;
                     cont = NativeMethods.Process32NextW(snapshot.DangerousGetHandle(), ref pe))
                {
                    if (!string.Equals(pe.szExeFile, "winlogon.exe", StringComparison.OrdinalIgnoreCase))
                        continue;

                    using (var proc = new SafeKernelHandle(
                               NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false,
                                   pe.th32ProcessID)))
                    {
                        if (proc.IsInvalid) continue;

                        IntPtr hToken;
                        if (!NativeMethods.OpenProcessToken(proc.DangerousGetHandle(),
                                NativeMethods.TOKEN_QUERY | NativeMethods.TOKEN_DUPLICATE, out hToken))
                            continue;

                        using (var tok = new SafeKernelHandle(hToken))
                        {
                            // 必须是同会话，且确实拥有 SeTcbPrivilege
                            if (TokenUtil.GetSessionId(tok) != selfSession) continue;
                            bool hasTcb;
                            if (!TokenUtil.CheckPrivilege(tok, NativeMethods.SE_TCB_NAME, out hasTcb) || !hasTcb)
                                continue;

                            IntPtr dup;
                            if (!NativeMethods.DuplicateTokenEx(hToken,
                                    NativeMethods.TOKEN_IMPERSONATE | NativeMethods.TOKEN_QUERY |
                                    NativeMethods.TOKEN_DUPLICATE,
                                    IntPtr.Zero, NativeMethods.SecurityImpersonation, NativeMethods.TokenImpersonation,
                                    out dup))
                            {
                                error = Win32.LastError;
                                return null;
                            }

                            var identity = new WinlogonSystemIdentity(new SafeKernelHandle(dup));
                            if (!NativeMethods.SetThreadToken(IntPtr.Zero, dup))
                            {
                                error = Win32.LastError;
                                identity.Dispose();
                                return null;
                            }

                            return identity;
                        }
                    }
                }
            }
        }

        error = NativeMethods.ERROR_ACCESS_DENIED; // 没找到可用 winlogon
        return null;
    }
}