using System;
using System.Runtime.InteropServices;

namespace IccEvolved.UiAccess.Tokens;

/// <summary>令牌查询/设置/权限的公共工具。</summary>
internal static class TokenUtil
{
    // ---- 查询 ----

    /// <summary>读取令牌信息（返回结构体值）。失败返回 default 并记录错误。</summary>
    public static bool GetTokenInfo<T>(SafeKernelHandle token, int infoClass, out T value, out int error)
        where T : struct
    {
        value = default;
        var size = (uint)Marshal.SizeOf(typeof(T));
        var buf = Marshal.AllocHGlobal((int)size);
        try
        {
            uint retLen;
            if (!NativeMethods.GetTokenInformation(token.DangerousGetHandle(), infoClass, buf, size, out retLen))
            {
                error = Win32.LastError;
                return false;
            }

            value = (T)Marshal.PtrToStructure(buf, typeof(T));
            error = 0;
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    public static bool IsUiAccess(SafeKernelHandle token)
    {
        int error;
        bool value;
        if (GetTokenInfo(token, NativeMethods.TokenUIAccess, out value, out error))
            return value;
        return false;
    }

    public static bool IsElevated(SafeKernelHandle token)
    {
        NativeMethods.TOKEN_ELEVATION elevation;
        int error;
        if (GetTokenInfo(token, NativeMethods.TokenElevation, out elevation, out error))
            return elevation.TokenIsElevated != 0;
        return false;
    }

    public static uint GetSessionId(SafeKernelHandle token)
    {
        uint session;
        int error;
        if (GetTokenInfo(token, NativeMethods.TokenSessionId, out session, out error))
            return session;
        return 0;
    }

    /// <summary>TokenElevationType：1 Default / 2 Full / 3 Limited。</summary>
    public static int GetElevationType(SafeKernelHandle token)
    {
        int type;
        int error;
        if (GetTokenInfo(token, NativeMethods.TokenElevationType, out type, out error))
            return type;
        return 0;
    }

    /// <summary>当前进程令牌（TOKEN_QUERY）。</summary>
    public static SafeKernelHandle OpenSelfToken(uint desiredAccess = NativeMethods.TOKEN_QUERY)
    {
        IntPtr h;
        if (!NativeMethods.OpenProcessToken(GetCurrentProcessHandle(), desiredAccess, out h))
            return null;
        return new SafeKernelHandle(h);
    }

    /// <summary>伪句柄 -1 不关闭，仅用于 OpenProcessToken 等 API。</summary>
    private static IntPtr GetCurrentProcessHandle()
    {
        return new IntPtr(-1);
    }

    /// <summary>检查令牌是否拥有指定特权（PrivilegeCheck）。</summary>
    public static bool CheckPrivilege(SafeKernelHandle token, string privilegeName, out bool present)
    {
        present = false;
        NativeMethods.LUID luid;
        if (!NativeMethods.LookupPrivilegeValue(null, privilegeName, out luid))
            return false;
        var ps = new NativeMethods.PRIVILEGE_SET
        {
            PrivilegeCount = 1,
            Control = 1, // PRIVILEGE_SET_ALL_NECESSARY
            Privilege = new NativeMethods.LUID_AND_ATTRIBUTES { Luid = luid, Attributes = 0 }
        };
        return NativeMethods.PrivilegeCheck(token.DangerousGetHandle(), ref ps, out present);
    }

    /// <summary>当前进程是否拥有 SeTcbPrivilege（SYSTEM 上下文标志）。</summary>
    public static bool CurrentProcessHasSeTcb()
    {
        using (var token = OpenSelfToken())
        {
            if (token == null || token.IsInvalid) return false;
            bool present;
            return CheckPrivilege(token, NativeMethods.SE_TCB_NAME, out present) && present;
        }
    }

    // ---- 设置 ----

    /// <summary>设置 TokenUIAccess。调用线程必须处于 SYSTEM 上下文（SeTcbPrivilege），否则失败。</summary>
    public static bool SetUiAccess(SafeKernelHandle primaryToken, bool value, out int error)
    {
        var size = Marshal.SizeOf(typeof(bool)); // BOOL = 4 字节；sizeof(bool) 在 C# 里是 1，会触发 ERROR_BAD_LENGTH
        var buf = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.WriteInt32(buf, value ? 1 : 0);
            if (!NativeMethods.SetTokenInformation(primaryToken.DangerousGetHandle(), NativeMethods.TokenUIAccess, buf,
                    (uint)size))
            {
                error = Win32.LastError;
                return false;
            }

            error = 0;
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    /// <summary>启用令牌的全部特权（枚举 TokenPrivileges 逐个 AdjustTokenPrivileges）。</summary>
    public static bool EnableAllPrivileges(SafeKernelHandle token, out int error)
    {
        error = 0;
        var hToken = token.DangerousGetHandle();

        // 第一次查询拿长度
        uint size = 0;
        if (!NativeMethods.GetTokenInformation(hToken, NativeMethods.TokenPrivileges, IntPtr.Zero, 0, out size))
            if (Win32.LastError != NativeMethods.ERROR_INSUFFICIENT_BUFFER)
            {
                error = Win32.LastError;
                return false;
            }

        var buf = Marshal.AllocHGlobal((int)size);
        try
        {
            uint retLen;
            if (!NativeMethods.GetTokenInformation(hToken, NativeMethods.TokenPrivileges, buf, size, out retLen))
            {
                error = Win32.LastError;
                return false;
            }

            var count = (uint)Marshal.ReadInt32(buf);
            var offset = sizeof(uint); // PrivilegeCount 之后是 LUID_AND_ATTRIBUTES 数组
            var ok = true;
            for (uint i = 0; i < count; i++)
            {
                var tp = new NativeMethods.TOKEN_PRIVILEGES { PrivilegeCount = 1 };
                tp.Privileges1 = (NativeMethods.LUID_AND_ATTRIBUTES)Marshal.PtrToStructure(
                    IntPtr.Add(buf, offset), typeof(NativeMethods.LUID_AND_ATTRIBUTES));
                tp.Privileges1.Attributes = NativeMethods.SE_PRIVILEGE_ENABLED;
                offset += Marshal.SizeOf(typeof(NativeMethods.LUID_AND_ATTRIBUTES));

                var stBuf = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(NativeMethods.TOKEN_PRIVILEGES)));
                try
                {
                    Marshal.StructureToPtr(tp, stBuf, false);
                    if (!NativeMethods.AdjustTokenPrivileges(hToken, false, ref tp,
                            (uint)Marshal.SizeOf(typeof(NativeMethods.TOKEN_PRIVILEGES)), IntPtr.Zero, IntPtr.Zero))
                        ok = false;
                    else if (Win32.LastError == NativeMethods.ERROR_NOT_ALL_ASSIGNED)
                        ok = false;
                }
                finally
                {
                    Marshal.FreeHGlobal(stBuf);
                }
            }

            error = ok ? 0 : NativeMethods.ERROR_NOT_ALL_ASSIGNED;
            return ok;
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    // ---- 复制 ----

    /// <summary>把任意令牌复制为主令牌（TokenPrimary, SecurityAnonymous）。</summary>
    public static SafeKernelHandle DuplicateAsPrimary(SafeKernelHandle source, uint desiredAccess)
    {
        IntPtr dup;
        if (!NativeMethods.DuplicateTokenEx(source.DangerousGetHandle(), desiredAccess, IntPtr.Zero,
                NativeMethods.SecurityAnonymous, NativeMethods.TokenPrimary, out dup))
            return null;
        return new SafeKernelHandle(dup);
    }
}