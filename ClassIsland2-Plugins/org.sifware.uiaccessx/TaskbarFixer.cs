using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Threading;

namespace Org.Sifware.UiAccessX;

/// <summary>
/// 修复 UIAccess 下「顶层效果窗口」等带 WS_EX_TOOLWINDOW 的窗口错误出现在任务栏的问题。
/// 通过 WinEvent 钩住本进程窗口的创建/显示，并用 ITaskbarList::DeleteTab 强制移出任务栏。
/// </summary>
[SupportedOSPlatform("windows")]
internal static class TaskbarFixer
{
    private const uint EVENT_OBJECT_CREATE = 0x8000;
    private const uint EVENT_OBJECT_SHOW = 0x8002;
    private const int OBJID_WINDOW = 0;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;

    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TOOLWINDOW = 0x00000080;
    private const uint COINIT_APARTMENTTHREADED = 0x2;

    private static readonly int OwnPid = Environment.ProcessId;

    private static WinEventProc? _winEventProc;
    private static IntPtr _createHook;
    private static IntPtr _showHook;
    private static DispatcherTimer? _sweepTimer;
    private static ITaskbarList? _taskbar;
    private static bool _started;

    public static void Start()
    {
        if (_started || !OperatingSystem.IsWindows())
        {
            return;
        }

        _started = true;
        _winEventProc = OnWinEvent;

        _createHook = SetWinEventHook(EVENT_OBJECT_CREATE, EVENT_OBJECT_CREATE, IntPtr.Zero, _winEventProc, 0, 0,
            WINEVENT_OUTOFCONTEXT);
        _showHook = SetWinEventHook(EVENT_OBJECT_SHOW, EVENT_OBJECT_SHOW, IntPtr.Zero, _winEventProc, 0, 0,
            WINEVENT_OUTOFCONTEXT);

        // 兜底扫描：任务栏可能在控件样式变化后重新加回标签。
        _sweepTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _sweepTimer.Tick += (_, _) => Sweep();
        _sweepTimer.Start();

        Sweep();
    }

    public static void Stop()
    {
        if (!_started)
        {
            return;
        }

        _sweepTimer?.Stop();
        _sweepTimer = null;

        if (_createHook != IntPtr.Zero)
        {
            UnhookWinEvent(_createHook);
            _createHook = IntPtr.Zero;
        }

        if (_showHook != IntPtr.Zero)
        {
            UnhookWinEvent(_showHook);
            _showHook = IntPtr.Zero;
        }

        _started = false;
    }

    private static void OnWinEvent(IntPtr hWinEventHook, uint @event, IntPtr hwnd, int idObject, int idChild,
        uint dwEventThread, uint dwmsEventTime)
    {
        if (idObject != OBJID_WINDOW || idChild != 0 || hwnd == IntPtr.Zero)
        {
            return;
        }

        RemoveFromTaskbarIfToolWindow(hwnd);
    }

    private static void Sweep()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            EnumWindows((hwnd, _) =>
            {
                RemoveFromTaskbarIfToolWindow(hwnd);
                return true;
            }, IntPtr.Zero);
        }
        catch
        {
            // 枚举失败时忽略，等待下一次扫描。
        }
    }

    private static void RemoveFromTaskbarIfToolWindow(IntPtr hwnd)
    {
        try
        {
            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid != OwnPid)
            {
                return;
            }

            var exStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
            if ((exStyle & WS_EX_TOOLWINDOW) == 0)
            {
                return;
            }

            GetTaskbar()?.DeleteTab(hwnd);
        }
        catch
        {
            // 忽略单个窗口失败。
        }
    }

    private static ITaskbarList? GetTaskbar()
    {
        if (_taskbar != null)
        {
            return _taskbar;
        }

        try
        {
            _ = CoInitializeEx(IntPtr.Zero, COINIT_APARTMENTTHREADED);
            var clsid = new Guid("56FDF344-FD6D-11d0-958A-006097C9A090");
            var comType = Type.GetTypeFromCLSID(clsid);
            if (comType == null)
            {
                return null;
            }

            _taskbar = (ITaskbarList?)Activator.CreateInstance(comType);
            _taskbar?.HrInit();
        }
        catch
        {
            _taskbar = null;
        }

        return _taskbar;
    }

    private delegate void WinEventProc(IntPtr hWinEventHook, uint @event, IntPtr hwnd, int idObject, int idChild,
        uint dwEventThread, uint dwmsEventTime);

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [ComImport]
    [Guid("56FDF342-FD6D-11d0-958A-006097C9A090")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList
    {
        void HrInit();

        void AddTab(IntPtr hwnd);

        void DeleteTab(IntPtr hwnd);

        void ActivateTab(IntPtr hwnd);

        void SetActiveAlt(IntPtr hwnd);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
        WinEventProc lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(IntPtr pvReserved, uint dwCoInit);

    private static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex) =>
        IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : new IntPtr(GetWindowLong32(hWnd, nIndex));
}
