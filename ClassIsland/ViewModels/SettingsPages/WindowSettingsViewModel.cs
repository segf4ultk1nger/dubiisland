using System;
using System.Collections.ObjectModel;
using ClassIsland.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClassIsland.ViewModels.SettingsPages;

public class WindowSettingsViewModel : ObservableRecipient
{
    private ObservableCollection<MonitorInfo> _screens = new();

    public ObservableCollection<MonitorInfo> Screens
    {
        get => _screens;
        set
        {
            if (Equals(value, _screens)) return;
            _screens = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 当前系统是否支持阻止截图（需要 Windows 10 2004 / build 19041 及以上）。
    /// </summary>
    public bool IsWindowCaptureBlockingSupported { get; } = Environment.OSVersion.Version.Build >= 19041;
}
