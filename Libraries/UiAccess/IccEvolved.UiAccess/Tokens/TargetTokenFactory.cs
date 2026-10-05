using System;
using System.Runtime.InteropServices;

namespace IccEvolved.UiAccess.Tokens;

/// <summary>按 TargetTokenMode 构建最终进程的主令牌。</summary>
internal static class TargetTokenFactory
{
    /// <summary>主令牌复制时申请的访问权限（含 SetTokenInformation 与 AdjustTokenPrivileges 所需）。</summary>
    internal const uint PrimaryDesiredAccess =
        NativeMethods.TOKEN_ASSIGN_PRIMARY | NativeMethods.TOKEN_DUPLICATE | NativeMethods.TOKEN_QUERY |
        NativeMethods.TOKEN_ADJUST_DEFAULT | NativeMethods.TOKEN_ADJUST_SESSIONID |
        NativeMethods.TOKEN_ADJUST_PRIVILEGES;

    /// <summary>创建目标主令牌。失败返回 null 并输出错误码。</summary>
    public static SafeKernelHandle Create(TargetTokenMode mode, int targetPid, int sessionId, out int error)
    {
        error = 0;
        switch (mode)
        {
            case TargetTokenMode.SelfPid:
                return FromProcessPid(targetPid, out error);
            case TargetTokenMode.SelfCurrent:
                using (var self = TokenUtil.OpenSelfToken(NativeMethods.TOKEN_DUPLICATE | NativeMethods.TOKEN_QUERY))
                {
                    if (self == null || self.IsInvalid)
                    {
                        error = Win32.LastError;
                        return null;
                    }

                    return TokenUtil.DuplicateAsPrimary(self, PrimaryDesiredAccess);
                }
            case TargetTokenMode.SessionFull:
                return FromWtsSession(sessionId, true, out error);
            case TargetTokenMode.SessionFiltered:
                return FromWtsSession(sessionId, false, out error);
            case TargetTokenMode.ExplorerDeElevated:
                return FromExplorerOrCtfmon(sessionId, out error);
            default:
                error = 87; // ERROR_INVALID_PARAMETER
                return null;
        }
    }

    /// <summary>按 PID 复制进程令牌为主令牌（保持原身份）。</summary>
    private static SafeKernelHandle FromProcessPid(int pid, out int error)
    {
        error = 0;
        using (var proc = new SafeKernelHandle(
                   NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)pid)))
        {
            if (proc.IsInvalid)
            {
                error = Win32.LastError;
                return null;
            }

            IntPtr hToken;
            if (!NativeMethods.OpenProcessToken(proc.DangerousGetHandle(),
                    NativeMethods.TOKEN_DUPLICATE | NativeMethods.TOKEN_QUERY, out hToken))
            {
                error = Win32.LastError;
                return null;
            }

            using (var tok = new SafeKernelHandle(hToken))
            {
                var dup = TokenUtil.DuplicateAsPrimary(tok, PrimaryDesiredAccess);
                if (dup == null) error = Win32.LastError;
                return dup;
            }
        }
    }

    /// <summary>WTSQueryUserToken 获取会话用户令牌（需要 SeTcbPrivilege，仅服务/SYSTEM 上下文可用）。</summary>
    private static SafeKernelHandle FromWtsSession(int sessionId, bool wantLinked, out int error)
    {
        error = 0;
        IntPtr hWts;
        if (!NativeMethods.WTSQueryUserToken((uint)sessionId, out hWts))
        {
            error = Win32.LastError;
            return null;
        }

        using (var wts = new SafeKernelHandle(hWts))
        {
            if (!wantLinked)
            {
                var dup = TokenUtil.DuplicateAsPrimary(wts, NativeMethods.TOKEN_ALL_ACCESS);
                if (dup == null) error = Win32.LastError;
                return dup;
            }

            // 取链接令牌（完整管理员令牌）
            NativeMethods.TOKEN_LINKED_TOKEN linked;
            if (!TokenUtil.GetTokenInfo(wts, NativeMethods.TokenLinkedToken, out linked, out error))
                return null;
            using (var linkedTok = new SafeKernelHandle(linked.LinkedToken))
            {
                var dup = TokenUtil.DuplicateAsPrimary(linkedTok, NativeMethods.TOKEN_ALL_ACCESS);
                if (dup == null) error = Win32.LastError;
                return dup;
            }
        }
    }

    /// <summary>从 explorer.exe / ctfmon.exe 获取非特权（降权）用户令牌，过滤掉以管理员运行的实例。</summary>
    private static SafeKernelHandle FromExplorerOrCtfmon(int sessionId, out int error)
    {
        error = 0;
        string[] candidates = { "explorer.exe", "ctfmon.exe" };
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
                var isCandidate = false;
                foreach (var name in candidates)
                    if (string.Equals(pe.szExeFile, name, StringComparison.OrdinalIgnoreCase))
                    {
                        isCandidate = true;
                        break;
                    }

                if (!isCandidate) continue;

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
                        if ((int)TokenUtil.GetSessionId(tok) != sessionId) continue;
                        // 跳过完全提升（管理员）的实例，只取普通用户令牌
                        if (TokenUtil.GetElevationType(tok) == NativeMethods.TokenElevationTypeFull) continue;

                        var dup = TokenUtil.DuplicateAsPrimary(tok, PrimaryDesiredAccess);
                        if (dup != null) return dup;
                        error = Win32.LastError;
                        return null;
                    }
                }
            }
        }

        error = NativeMethods.ERROR_NOT_FOUND; // 1168
        return null;
    }
}