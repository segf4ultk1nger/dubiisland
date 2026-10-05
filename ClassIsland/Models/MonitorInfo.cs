using System.Windows.Forms;
using ClassIsland.Helpers;

namespace ClassIsland.Models;

public class MonitorInfo
{
    public MonitorInfo(Screen screen)
    {
        Screen = screen;
        FriendlyName = MonitorNameHelper.GetFriendlyName(screen.DeviceName);
    }

    public Screen Screen { get; }

    public string FriendlyName { get; }

    public int Width => Screen.Bounds.Width;

    public int Height => Screen.Bounds.Height;
}
