using System;
using System.Runtime.InteropServices;

namespace ClassIsland.Helpers.Native;

public static class InjectionGuardNative
{
    public delegate void LdrDllNotification(uint reason, IntPtr notificationData, IntPtr context);

    [DllImport("ntdll.dll", ExactSpelling = true)]
    public static extern uint LdrRegisterDllNotification(uint flags, IntPtr callback, IntPtr context, out IntPtr cookie);

    [DllImport("ntdll.dll", ExactSpelling = true)]
    public static extern uint LdrUnregisterDllNotification(IntPtr cookie);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool FreeLibrary(IntPtr hModule);

    public const int ProcessExtensionPointDisablePolicy = 6;

    [StructLayout(LayoutKind.Sequential)]
    public struct PROCESS_MITIGATION_EXTENSION_POINT_DISABLE_POLICY
    {
        public uint Flags;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessMitigationPolicy(int mitigationPolicy, ref PROCESS_MITIGATION_EXTENSION_POINT_DISABLE_POLICY lpBuffer, IntPtr dwLength);

    public static bool TryUnloadModule(IntPtr baseAddress)
    {
        try
        {
            return FreeLibrary(baseAddress);
        }
        catch
        {
            return false;
        }
    }

    public static bool TryDisableExtensionPoints()
    {
        try
        {
            var policy = new PROCESS_MITIGATION_EXTENSION_POINT_DISABLE_POLICY { Flags = 1 };
            return SetProcessMitigationPolicy(ProcessExtensionPointDisablePolicy, ref policy, (IntPtr)Marshal.SizeOf<PROCESS_MITIGATION_EXTENSION_POINT_DISABLE_POLICY>());
        }
        catch
        {
            return false;
        }
    }
}
