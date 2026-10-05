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
        SettingsService.Settings.PropertyChanged += SettingsOnPropertyChanged;
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
        ViewModel.Screens = new ObservableCollection<MonitorInfo>(Screen.AllScreens.Select(s => new MonitorInfo(s)));
    }

    private void ButtonRestart_OnClick(object sender, RoutedEventArgs e)
    {
        RequestRestart();
    }
}
