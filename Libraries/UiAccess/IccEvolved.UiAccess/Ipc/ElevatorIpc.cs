using System;
using System.Runtime.InteropServices;
using IccEvolved.UiAccess.Elevator;

namespace IccEvolved.UiAccess.Ipc;

/// <summary>
///     提权中间人与一次性服务之间的共享内存结构（Global\ 命名文件映射）。
///     请求字段由中间人写入，服务进程（SYSTEM）执行后写回结果字段。
/// </summary>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct ElevatorIpc
{
    // ---- 结果（服务写回） ----
    public int Magic; // ElevatorIpcMagic 校验
    public int Status; // ElevatorStatusPending/Failed/Success
    public int Error;
    public int ChildPid;

    // ---- 请求（中间人写入） ----
    public int TargetTokenMode;
    public int TargetPid;
    public int SessionId;
    public int UiAccess; // 0/1
    public int LoadProfile; // 0/1
    public int NewConsole; // 0/1
    public int ExtraCreateFlags;
    public int EnableAllPrivilegesOnTarget;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
    public string TargetExe;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 1024)]
    public string TargetCmdLine;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
    public string Desktop;

    public const int FileMapSize = 4096;

    public static ElevatorIpc FromJobArgs(ElevatorJobArgs a)
    {
        return new ElevatorIpc
        {
            Magic = NativeMethods.ElevatorIpcMagic,
            Status = NativeMethods.ElevatorStatusPending,
            TargetTokenMode = (int)a.TargetMode,
            TargetPid = a.TargetPid,
            SessionId = a.SessionId,
            UiAccess = a.UiAccess ? 1 : 0,
            LoadProfile = a.LoadProfile ? 1 : 0,
            NewConsole = a.NewConsole ? 1 : 0,
            ExtraCreateFlags = unchecked((int)a.ExtraCreateFlags),
            EnableAllPrivilegesOnTarget = a.EnableAllPrivilegesOnTarget ? 1 : 0,
            TargetExe = a.TargetExe ?? "",
            TargetCmdLine = a.FullCommandLine ?? "",
            Desktop = a.Desktop ?? ""
        };
    }
}

/// <summary>Global\ 命名文件映射封装。</summary>
internal sealed class NamedFileMap : IDisposable
{
    private readonly SafeKernelHandle _mapping;
    private readonly bool _owner;
    private IntPtr _base;

    private NamedFileMap(SafeKernelHandle mapping, IntPtr baseAddr, bool owner)
    {
        _mapping = mapping;
        _base = baseAddr;
        _owner = owner;
    }

    public void Dispose()
    {
        if (_base != IntPtr.Zero)
        {
            NativeMethods.UnmapViewOfFile(_base);
            _base = IntPtr.Zero;
        }

        _mapping?.Dispose();
    }

    /// <summary>创建映射（中间人侧）。name 不含 Global\ 前缀。</summary>
    public static NamedFileMap Create(string name)
    {
        var map = NativeMethods.CreateFileMappingW(new IntPtr(-1), IntPtr.Zero,
            NativeMethods.PAGE_READWRITE | NativeMethods.SEC_COMMIT, 0, ElevatorIpc.FileMapSize, "Global\\" + name);
        if (map == IntPtr.Zero) return null;
        var baseAddr = NativeMethods.MapViewOfFile(map, NativeMethods.FILE_MAP_ALL_ACCESS, 0, 0, UIntPtr.Zero);
        if (baseAddr == IntPtr.Zero)
        {
            NativeMethods.CloseHandle(map);
            return null;
        }

        return new NamedFileMap(new SafeKernelHandle(map), baseAddr, true);
    }

    /// <summary>打开已有映射（服务进程侧）。</summary>
    public static NamedFileMap Open(string name)
    {
        var map = NativeMethods.OpenFileMappingW(NativeMethods.FILE_MAP_ALL_ACCESS, false, "Global\\" + name);
        if (map == IntPtr.Zero) return null;
        var baseAddr = NativeMethods.MapViewOfFile(map, NativeMethods.FILE_MAP_ALL_ACCESS, 0, 0, UIntPtr.Zero);
        if (baseAddr == IntPtr.Zero)
        {
            NativeMethods.CloseHandle(map);
            return null;
        }

        return new NamedFileMap(new SafeKernelHandle(map), baseAddr, false);
    }

    public ElevatorIpc Read()
    {
        return (ElevatorIpc)Marshal.PtrToStructure(_base, typeof(ElevatorIpc));
    }

    public void Write(ElevatorIpc value)
    {
        Marshal.StructureToPtr(value, _base, false);
    }
}