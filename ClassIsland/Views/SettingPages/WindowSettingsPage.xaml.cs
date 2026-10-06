using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Forms;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Enums.SettingsWindow;
using ClassIsland.Models;
using ClassIsland.Services;
using ClassIsland.ViewModels.SettingsPages;

using ClassIsland.Core.Controls;
namespace ClassIsland.Views.SettingPages;

/// <summary>
/// WindowSettingsPage.xaml 的交互逻辑
/// </summary>
[SettingsPageInfo("window", "窗口", IconGlyphs.WindowMaximize, IconGlyphs.WindowMaximize, SettingsPageCategory.Internal)]
public partial class WindowSettingsPage : SettingsPageBase
{
    public SettingsService SettingsService { get; }

    public WindowSettingsViewModel ViewModel { get; } = new();

    public WindowSettingsPage(SettingsService settingsService)
    {
        InitializeComponent();
        DataContext = this;
        SettingsService = settingsService;
        RefreshScreens();
        if (!ViewModel.IsWindowCaptureBlockingSupported && SettingsService.Settings.IsWindowCaptureBlockingEnabled)
        {
            SettingsService.Settings.IsWindowCaptureBlockingEnabled = false;
        }
        Loaded += WindowSettingsPage_OnLoaded;
        Unloaded += WindowSettingsPage_OnUnloaded;
    }

    private void WindowSettingsPage_OnLoaded(object sender, RoutedEventArgs e)
    {
        SettingsService.Settings.PropertyChanged -= SettingsOnPropertyChanged;
        SettingsService.Settings.PropertyChanged += SettingsOnPropertyChanged;
    }

    private void WindowSettingsPage_OnUnloaded(object sender, RoutedEventArgs e)
    {
        SettingsService.Settings.PropertyChanged -= SettingsOnPropertyChanged;
    }

    private void SettingsOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SettingsService.Settings.UseRawInput) or nameof(SettingsService.Settings.IsCompatibleWindowTransparentEnabled))
        {
            RequestRestart();
        }
    }

    private void ButtonRefreshMonitors_OnClick(object sender, RoutedEventArgs e)
    {
        RefreshScreens();
    }

    private void RefreshScreens()
    {
        var index = SettingsService.Settings.WindowDockingMonitorIndex;
        ViewModel.Screens = new ObservableCollection<MonitorInfo>(Screen.AllScreens.Select(s => new MonitorInfo(s)));
        // 重建列表会让 ComboBox 清空选中并把 -1 写回设置，这里恢复原索引（越界则收敛到有效范围）。
        // 由于已移除拦截负值的校验规则，-1 会先落到设置、再被这里改回有效值并触发通知，下拉框随之重新选中。
        var max = Math.Max(0, ViewModel.Screens.Count - 1);
        SettingsService.Settings.WindowDockingMonitorIndex = Math.Max(0, Math.Min(index, max));
    }

    private void ButtonRestart_OnClick(object sender, RoutedEventArgs e)
    {
        RequestRestart();
    }
}
