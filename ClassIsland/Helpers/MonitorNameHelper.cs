using System;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;

namespace ClassIsland.Helpers;

internal static class MonitorNameHelper
{
    private const uint EDD_GET_DEVICE_INTERFACE_NAME = 0x00000001;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

    /// <summary>
    /// 获取显示器友好名称（如 "ASUS XQ32MRY"）。获取失败时返回传入的设备名。
    /// </summary>
    public static string GetFriendlyName(string deviceName)
    {
        var friendly = TryGetWmiMonitorName(deviceName);
        if (!string.IsNullOrWhiteSpace(friendly))
        {
            return friendly;
        }

        var monitor = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
        if (EnumDisplayDevices(deviceName, 0, ref monitor, 0) &&
            !string.IsNullOrWhiteSpace(monitor.DeviceString) &&
            !string.Equals(monitor.DeviceString, "Generic PnP Monitor", StringComparison.OrdinalIgnoreCase))
        {
            return monitor.DeviceString;
        }

        return deviceName;
    }

    private static string TryGetWmiMonitorName(string deviceName)
    {
        try
        {
            var monitor = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
            if (!EnumDisplayDevices(deviceName, 0, ref monitor, EDD_GET_DEVICE_INTERFACE_NAME))
            {
                return "";
            }

            var hardwareId = ExtractHardwareId(monitor.DeviceID);
            if (string.IsNullOrEmpty(hardwareId))
            {
                return "";
            }

            using var searcher = new ManagementObjectSearcher(
                @"root\wmi", "SELECT InstanceName, UserFriendlyName FROM WmiMonitorID");
            foreach (var obj in searcher.Get())
            {
                var instanceName = obj["InstanceName"] as string ?? "";
                if (instanceName.IndexOf(hardwareId, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                if (obj["UserFriendlyName"] is ushort[] name)
                {
                    var result = DecodeEdidString(name);
                    if (!string.IsNullOrWhiteSpace(result))
                    {
                        return result;
                    }
                }
            }
        }
        catch
        {
            // WMI 不可用时忽略，退回 DeviceString。
        }

        return "";
    }

    private static string? ExtractHardwareId(string deviceId)
    {
        // "\\?\DISPLAY#ACI32A1#5&...&0&UID256_0#{e6f07b5f-...}" 或 "MONITOR\\ACI32A1\\{...}"
        foreach (var part in deviceId.Split('#', '\\'))
        {
            if (part.Length == 7 && part.All(char.IsLetterOrDigit))
            {
                return part;
            }
        }

        return null;
    }

    private static string DecodeEdidString(ushort[] data)
    {
        var builder = new StringBuilder();
        foreach (var c in data)
        {
            if (c == 0)
            {
                break;
            }

            builder.Append((char)c);
        }

        return builder.ToString().Trim();
    }
}
