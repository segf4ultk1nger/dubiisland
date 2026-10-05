using System;
using System.Runtime.ConstrainedExecution;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace IccEvolved.UiAccess;

/// <summary>全部 Win32 P/Invoke 声明、常量与结构。</summary>
internal static class NativeMethods
{
    // ---- 访问权限 ----
    internal const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    internal const uint PROCESS_QUERY_INFORMATION = 0x0400;
    internal const uint PROCESS_ALL_ACCESS = 0x1F0FFF;

    internal const uint TOKEN_ASSIGN_PRIMARY = 0x0001;
    internal const uint TOKEN_DUPLICATE = 0x0002;
    internal const uint TOKEN_IMPERSONATE = 0x0004;
    internal const uint TOKEN_QUERY = 0x0008;
    internal const uint TOKEN_ADJUST_DEFAULT = 0x0080;
    internal const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
    internal const uint TOKEN_ADJUST_SESSIONID = 0x0100;
    internal const uint TOKEN_ALL_ACCESS = 0x0F01FF;
    internal const uint MAXIMUM_ALLOWED = 0x02000000;

    internal const int SecurityAnonymous = 0;
    internal const int SecurityImpersonation = 2;
    internal const int TokenPrimary = 1;
    internal const int TokenImpersonation = 2;

    // ---- TOKEN_INFORMATION_CLASS ----
    internal const int TokenPrivileges = 3;
    internal const int TokenElevationType = 18;
    internal const int TokenLinkedToken = 19;
    internal const int TokenElevation = 20;
    internal const int TokenSessionId = 12;
    internal const int TokenUIAccess = 26;

    internal const int TokenElevationTypeFull = 2;

    // ---- 特权 ----
    internal const uint SE_PRIVILEGE_ENABLED = 0x00000002;
    internal const string SE_TCB_NAME = "SeTcbPrivilege";
    internal const string SE_IMPERSONATE_NAME = "SeImpersonatePrivilege";

    // ---- 进程创建 ----
    internal const uint CREATE_NEW_CONSOLE = 0x00000010;
    internal const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
    internal const uint CREATE_SUSPENDED = 0x00000004;
    internal const uint LOGON_WITH_PROFILE = 0x00000001;

    // ---- Toolhelp ----
    internal const uint TH32CS_SNAPPROCESS = 0x00000002;

    // ---- 文件映射 ----
    internal const uint PAGE_READWRITE = 0x04;
    internal const uint SEC_COMMIT = 0x08000000;
    internal const uint FILE_MAP_ALL_ACCESS = 0xF001F;

    // ---- SCM ----
    internal const uint SC_MANAGER_ALL_ACCESS = 0xF003F;
    internal const uint SERVICE_ALL_ACCESS = 0xF01FF;
    internal const uint SERVICE_WIN32_OWN_PROCESS = 0x10;
    internal const uint SERVICE_DEMAND_START = 0x3;
    internal const uint SERVICE_ERROR_IGNORE = 0x0;

    internal const uint SERVICE_STOPPED = 0x1;
    internal const uint SERVICE_START_PENDING = 0x2;
    internal const uint SERVICE_RUNNING = 0x4;
    internal const uint SERVICE_ACCEPT_STOP = 0x1;

    // ---- ShellExecuteEx ----
    internal const uint SEE_MASK_NOCLOSEPROCESS = 0x40;
    internal const int SW_SHOWNORMAL = 1;

    // ---- 错误 ----
    internal const int ERROR_SUCCESS = 0;
    internal const int ERROR_ACCESS_DENIED = 5;
    internal const int ERROR_INSUFFICIENT_BUFFER = 122;
    internal const int ERROR_NOT_ALL_ASSIGNED = 1300;
    internal const int ERROR_CANCELLED = 1223;
    internal const int ERROR_NOT_FOUND = 1168;
    internal const int ERROR_TIMEOUT = 1460;
    internal const int ERROR_PRIVILEGE_NOT_HELD = 1314;

    // ---- IPC ----
    internal const int ElevatorIpcMagic = 0x55494143; // "UIA\0"
    internal const int ElevatorStatusPending = 0;
    internal const int ElevatorStatusFailed = 0x10;
    internal const int ElevatorStatusSuccess = 0x50;

    // ---- kernel32 ----
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool Process32FirstW(IntPtr hSnapshot, ref PROCESSENTRY32W lppe);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool Process32NextW(IntPtr hSnapshot, ref PROCESSENTRY32W lppe);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool ProcessIdToSessionId(uint dwProcessId, out uint pSessionId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern IntPtr CreateFileMappingW(IntPtr hFile, IntPtr lpAttributes, uint flProtect,
        uint dwMaximumSizeHigh, uint dwMaximumSizeLow, string lpName);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern IntPtr OpenFileMappingW(uint dwDesiredAccess, bool bInheritHandle, string lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr MapViewOfFile(IntPtr hFileMappingObject, uint dwDesiredAccess,
        uint dwFileOffsetHigh, uint dwFileOffsetLow, UIntPtr dwNumberOfBytesToMap);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool UnmapViewOfFile(IntPtr lpBaseAddress);

    // ---- advapi32: 令牌 ----
    [DllImport("advapi32.dll", SetLastError = true)]
    internal static extern bool OpenProcessToken(IntPtr hProcess, uint dwDesiredAccess, out IntPtr hToken);

    [DllImport("advapi32.dll", SetLastError = true)]
    internal static extern bool DuplicateTokenEx(IntPtr hExistingToken, uint dwDesiredAccess,
        IntPtr lpTokenAttributes, int ImpersonationLevel, int TokenType, out IntPtr phNewToken);

    [DllImport("advapi32.dll", SetLastError = true)]
    internal static extern bool GetTokenInformation(IntPtr hToken, int TokenInformationClass,
        IntPtr pTokenInformation, uint TokenInformationLength, out uint ReturnLength);

    [DllImport("advapi32.dll", SetLastError = true)]
    internal static extern bool SetTokenInformation(IntPtr hToken, int TokenInformationClass,
        IntPtr pTokenInformation, uint TokenInformationLength);

    [DllImport("advapi32.dll", SetLastError = true)]
    internal static extern bool SetThreadToken(IntPtr hThread, IntPtr hToken);

    [DllImport("advapi32.dll", SetLastError = true)]
    internal static extern bool RevertToSelf();

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern bool LookupPrivilegeValue(string lpSystemName, string lpName, out LUID lpLuid);

    [DllImport("advapi32.dll", SetLastError = true)]
    internal static extern bool AdjustTokenPrivileges(IntPtr TokenHandle, bool DisableAllPrivileges,
        ref TOKEN_PRIVILEGES NewState, uint BufferLength, IntPtr PreviousState, IntPtr ReturnLength);

    [DllImport("advapi32.dll", SetLastError = true)]
    internal static extern bool PrivilegeCheck(IntPtr ClientToken, ref PRIVILEGE_SET RequiredPrivileges,
        out bool pfResult);

    // ---- advapi32: 进程创建 ----
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern bool CreateProcessWithTokenW(IntPtr hToken, uint dwLogonFlags,
        string lpApplicationName, StringBuilder lpCommandLine, uint dwCreationFlags,
        IntPtr lpEnvironment, string lpCurrentDirectory, ref STARTUPINFO lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern bool CreateProcessAsUserW(IntPtr hToken, string lpApplicationName,
        StringBuilder lpCommandLine, IntPtr lpProcessAttributes, IntPtr lpThreadAttributes,
        bool bInheritHandles, uint dwCreationFlags, IntPtr lpEnvironment, string lpCurrentDirectory,
        ref STARTUPINFO lpStartupInfo, out PROCESS_INFORMATION lpProcessInformation);

    // ---- advapi32: SCM ----
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern IntPtr OpenSCManagerW(string lpMachineName, string lpDatabaseName, uint dwDesiredAccess);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern IntPtr CreateServiceW(IntPtr hSCManager, string lpServiceName, string lpDisplayName,
        uint dwDesiredAccess, uint dwServiceType, uint dwStartType, uint dwErrorControl, string lpBinaryPathName,
        string lpLoadOrderGroup, IntPtr lpdwTagId, string lpDependencies, string lpServiceStartName, string lpPassword);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern IntPtr OpenServiceW(IntPtr hSCManager, string lpServiceName, uint dwDesiredAccess);

    [DllImport("advapi32.dll", SetLastError = true)]
    internal static extern bool StartServiceW(IntPtr hService, uint dwNumServiceArgs, string[] lpServiceArgVectors);

    [DllImport("advapi32.dll", SetLastError = true)]
    internal static extern bool DeleteService(IntPtr hService);

    [DllImport("advapi32.dll", SetLastError = true)]
    internal static extern bool CloseServiceHandle(IntPtr hSCObject);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern bool StartServiceCtrlDispatcherW([In] SERVICE_TABLE_ENTRY[] lpServiceTable);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern IntPtr RegisterServiceCtrlHandlerW(string lpServiceName,
        ServiceControlHandlerDelegate lpHandlerProc);

    [DllImport("advapi32.dll", SetLastError = true)]
    internal static extern bool SetServiceStatus(IntPtr hServiceStatus, ref SERVICE_STATUS lpServiceStatus);

    // ---- wtsapi32 / userenv ----
    [DllImport("wtsapi32.dll", SetLastError = true)]
    internal static extern bool WTSQueryUserToken(uint sessionId, out IntPtr phToken);

    [DllImport("userenv.dll", SetLastError = true)]
    internal static extern bool CreateEnvironmentBlock(out IntPtr lpEnvironment, IntPtr hToken, bool bInherit);

    [DllImport("userenv.dll", SetLastError = true)]
    internal static extern bool DestroyEnvironmentBlock(IntPtr lpEnvironment);

    // ---- shell32 ----
    [DllImport("shell32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern bool ShellExecuteExW(ref SHELLEXECUTEINFO lpExecInfo);

    [DllImport("kernel32.dll")]
    internal static extern uint GetLastError();

    // ---- 进程快照 ----
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct PROCESSENTRY32W
    {
        internal uint dwSize;
        internal uint cntUsage;
        internal uint th32ProcessID;
        internal IntPtr th32DefaultHeapID;
        internal uint th32ModuleID;
        internal uint cntThreads;
        internal uint th32ParentProcessID;
        internal int pcPriClassBase;
        internal uint dwFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        internal string szExeFile;
    }

    // ---- 令牌 ----
    [StructLayout(LayoutKind.Sequential)]
    internal struct LUID
    {
        internal uint LowPart;
        internal int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct LUID_AND_ATTRIBUTES
    {
        internal LUID Luid;
        internal uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct TOKEN_PRIVILEGES
    {
        internal uint PrivilegeCount;
        internal LUID_AND_ATTRIBUTES Privileges1; // 定长 1，动态用缓冲区
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PRIVILEGE_SET
    {
        internal uint PrivilegeCount;
        internal uint Control;
        internal LUID_AND_ATTRIBUTES Privilege;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct TOKEN_ELEVATION
    {
        internal uint TokenIsElevated;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct TOKEN_LINKED_TOKEN
    {
        internal IntPtr LinkedToken;
    }

    // ---- 启动信息 ----
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct STARTUPINFO
    {
        internal int cb;
        internal IntPtr lpReserved;
        internal string lpDesktop;
        internal IntPtr lpTitle;
        internal int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        internal short wShowWindow;
        internal short cbReserved2;
        internal IntPtr lpReserved2;
        internal IntPtr hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PROCESS_INFORMATION
    {
        internal IntPtr hProcess;
        internal IntPtr hThread;
        internal uint dwProcessId;
        internal uint dwThreadId;
    }

    // ---- 服务 ----
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct SERVICE_TABLE_ENTRY
    {
        [MarshalAs(UnmanagedType.LPWStr)] internal string lpServiceName;
        internal ServiceMainDelegate lpServiceProc;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SERVICE_STATUS
    {
        internal uint dwServiceType;
        internal uint dwCurrentState;
        internal uint dwControlsAccepted;
        internal uint dwWin32ExitCode;
        internal uint dwServiceSpecificExitCode;
        internal uint dwCheckPoint;
        internal uint dwWaitHint;
    }

    internal delegate void ServiceMainDelegate(int argc, IntPtr argv);

    internal delegate void ServiceControlHandlerDelegate(int control);

    // ---- ShellExecuteEx ----
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct SHELLEXECUTEINFO
    {
        internal int cbSize;
        internal uint fMask;
        internal IntPtr hwnd;
        [MarshalAs(UnmanagedType.LPWStr)] internal string lpVerb;
        [MarshalAs(UnmanagedType.LPWStr)] internal string lpFile;
        [MarshalAs(UnmanagedType.LPWStr)] internal string lpParameters;
        [MarshalAs(UnmanagedType.LPWStr)] internal string lpDirectory;
        internal int nShow;
        internal IntPtr hInstApp;
        internal IntPtr lpIDList;
        [MarshalAs(UnmanagedType.LPWStr)] internal string lpClass;
        internal IntPtr hkeyClass;
        internal uint dwHotKey;
        internal IntPtr hIcon;
        internal IntPtr hProcess;
    }
}

// ---- SafeHandle 封装 ----

/// <summary>CloseHandle 释放的句柄。</summary>
internal sealed class SafeKernelHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    private SafeKernelHandle() : base(true)
    {
    }

    internal SafeKernelHandle(IntPtr handle, bool ownsHandle = true) : base(ownsHandle)
    {
        SetHandle(handle);
    }

    [ReliabilityContract(Consistency.WillNotCorruptState, Cer.MayFail)]
    protected override bool ReleaseHandle()
    {
        return NativeMethods.CloseHandle(handle);
    }
}

/// <summary>CloseServiceHandle 释放的服务句柄。</summary>
internal sealed class SafeServiceHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    private SafeServiceHandle() : base(true)
    {
    }

    internal SafeServiceHandle(IntPtr handle, bool ownsHandle = true) : base(ownsHandle)
    {
        SetHandle(handle);
    }

    [ReliabilityContract(Consistency.WillNotCorruptState, Cer.MayFail)]
    protected override bool ReleaseHandle()
    {
        return NativeMethods.CloseServiceHandle(handle);
    }
}

internal static class Win32
{
    /// <summary>读取当前线程最后一个 Win32 错误。</summary>
    public static int LastError => unchecked((int)NativeMethods.GetLastError());
}