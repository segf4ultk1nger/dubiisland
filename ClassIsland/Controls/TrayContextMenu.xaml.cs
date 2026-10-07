using System;
using System.Windows;
using System.Windows.Controls;
using ClassIsland.Core;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Abstractions.Services.Management;
using ClassIsland.Services;
using ClassIsland.ViewModels;
using ClassIsland.Views;

namespace ClassIsland.Controls;

public partial class TrayContextMenu : ResourceDictionary
{
    public MainViewModel ViewModel { get; } = App.GetService<MainViewModel>();

    public ILessonsService LessonsService { get; } = App.GetService<ILessonsService>();

    public TrayContextMenu()
    {
        InitializeComponent();
    }

    private void MenuItemAbout_OnClick(object sender, RoutedEventArgs e)
    {
        App.GetService<SettingsWindowNew>().Open("about");
    }

    private void MenuItemSwitchMainWindowVisibility_OnClick(object sender, RoutedEventArgs e)
    {
        ViewModel.Settings.IsMainWindowVisible = !ViewModel.Settings.IsMainWindowVisible;
    }

    private void MenuItemClearAllNotifications_OnClick(object sender, RoutedEventArgs e)
    {
        App.GetService<INotificationHostService>().CancelAllNotifications();
    }

    private void ButtonSettings_OnClick(object sender, RoutedEventArgs e)
    {
        App.GetService<ProfileSettingsWindow>().Open();
    }

    private void MenuItemSettings_OnClick(object sender, RoutedEventArgs e)
    {
        App.GetService<SettingsWindowNew>().Open();
    }

    private void MenuItemTemporaryClassPlan_OnClick(object sender, RoutedEventArgs e)
    {
        App.GetService<ProfileSettingsWindow>().OpenDrawer("TemporaryClassPlan");
        App.GetService<ProfileSettingsWindow>().Open();
    }

    private void MenuItemClassSwap_OnClick(object sender, RoutedEventArgs e)
    {
        App.GetService<TrayIconService>().OpenClassSwapWindow();
    }

    private void MenuItemRestartApp_OnClick(object sender, RoutedEventArgs e)
    {
        AppBase.Current.Restart();
    }

    private async void MenuItemExitApp_OnClick(object sender, RoutedEventArgs e)
    {
        if (!await App.GetService<IManagementService>().AuthorizeByLevel(
                App.GetService<IManagementService>().CredentialConfig.ExitApplicationAuthorizeLevel))
        {
            return;
        }
        ViewModel.IsClosing = true;
        AppBase.Current.Stop();
    }

    private void ButtonResizeDebug_OnClick(object sender, RoutedEventArgs e)
    {
    }

    private void MenuItemDebugFitSize_OnClick(object sender, RoutedEventArgs e)
    {
        ViewModel.OverlayRemainTimePercents = 0.5;
    }

    private void MenuItemNotificationSettings_OnClick(object sender, RoutedEventArgs e)
    {
        App.GetService<SettingsWindowNew>().Open("notification");
    }

    private void MenuItemSettingsWindow2_OnClick(object sender, RoutedEventArgs e)
    {
        App.GetService<SettingsWindowNew>().Open();
    }
}
