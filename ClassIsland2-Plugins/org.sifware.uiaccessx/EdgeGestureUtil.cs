using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using ClassIsland.Core;

namespace Org.Sifware.UiAccessX;

/// <summary>
/// 通过 Shell 属性存储（IPropertyStore）设置窗口的「禁止在全屏时响应触摸边缘手势」属性
/// （<c>PKEY_EdgeGesture_DisableTouchWhenFullscreen</c>），以屏蔽 Windows 10 自带的屏幕边缘手势。
/// 移植自 IccEvolved 的 <c>EdgeGestureUtil</c>。
/// </summary>
internal static class EdgeGestureUtil
{
    private const short VtBool = 11;

    private static readonly Guid DisableTouchScreen = new("32CE38B2-2C9A-41B1-9BC5-B3784394AA44");
    private static readonly Guid IidPropertyStore = new("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99");

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern int SHGetPropertyStoreForWindow(IntPtr hwnd, ref Guid riid, ref IPropertyStore? propertyStore);

    public static void Start()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            AppBase.Current.AppStarted += (_, _) => Apply(UiAccessX.Settings.BlockEdgeGestures);
        }
        catch
        {
            // AppBase 在极早期可能不可用，忽略。
        }

        // 主窗口创建后再补一次，覆盖 AppStarted 过早、句柄尚未就绪的情况。
        Control.LoadedEvent.AddClassHandler<Window>((window, _) =>
        {
            if (ReferenceEquals(window, GetMainWindow()))
            {
                Apply(UiAccessX.Settings.BlockEdgeGestures);
            }
        });
    }

    /// <summary>按设置对主窗口应用/撤销边缘手势屏蔽。</summary>
    public static void Apply(bool disabled)
    {
        var hwnd = GetMainWindowHandle();
        if (hwnd != IntPtr.Zero)
        {
            SetDisabled(hwnd, disabled);
        }
    }

    public static void SetDisabled(IntPtr hwnd, bool disabled)
    {
        if (!OperatingSystem.IsWindows() || hwnd == IntPtr.Zero)
        {
            return;
        }

        IPropertyStore? store = null;
        try
        {
            var iid = IidPropertyStore;
            if (SHGetPropertyStoreForWindow(hwnd, ref iid, ref store) != 0 || store == null)
            {
                return;
            }

            var key = new PropertyKey { fmtid = DisableTouchScreen, pid = 2 };
            var value = new PropVariant { vt = VtBool, boolVal = disabled };
            store.SetValue(ref key, ref value);
            store.Commit();
        }
        catch
        {
            // 失败静默忽略。
        }
        finally
        {
            if (store != null)
            {
                Marshal.ReleaseComObject(store);
            }
        }
    }

    private static Window? GetMainWindow()
    {
        try
        {
            return (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        }
        catch
        {
            return null;
        }
    }

    private static IntPtr GetMainWindowHandle() => GetMainWindow()?.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;

    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        void GetCount(ref uint cProps);

        void GetAt(uint iProp, ref PropertyKey pkey);

        void GetValue(ref PropertyKey key, ref PropVariant pv);

        void SetValue(ref PropertyKey key, ref PropVariant pv);

        void Commit();
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct PropertyKey
    {
        [MarshalAs(UnmanagedType.Struct)] public Guid fmtid;
        public uint pid;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct PropVariant
    {
        [FieldOffset(0)] public short vt;
        [FieldOffset(2)] private short wReserved1;
        [FieldOffset(4)] private short wReserved2;
        [FieldOffset(6)] private short wReserved3;
        [FieldOffset(8)] public bool boolVal;
    }
}
