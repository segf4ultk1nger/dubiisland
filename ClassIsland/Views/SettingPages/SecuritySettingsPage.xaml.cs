using System.Windows;
using ClassIsland.Core.Abstractions.Services.Management;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Enums.SettingsWindow;
using ClassIsland.Services;
using ClassIsland.ViewModels.SettingsPages;

namespace ClassIsland.Views.SettingPages;

/// <summary>
/// SecuritySettingsPage.xaml 的交互逻辑
/// </summary>
[SettingsPageInfo("security", "安全", SettingsPageCategory.Internal)]
public partial class SecuritySettingsPage
{
    public SecuritySettingsViewModel ViewModel { get; } = new();
    public SettingsService SettingsService { get; }
    public IManagementService ManagementService { get; }

    public SecuritySettingsPage(SettingsService settingsService, IManagementService managementService)
    {
        SettingsService = settingsService;
        ManagementService = managementService;
        DataContext = this;
        InitializeComponent();
    }

    private async void SecuritySettingsPage_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (ManagementService.IsManagementEnabled)
        {
            return;
        }
        var result =
            await ManagementService.AuthorizeByLevel(ManagementService.CredentialConfig
                .EditSettingsAuthorizeLevel);
        if (result)
        {
            ViewModel.IsLocked = false;
        }
    }

    private void SecuritySettingsPage_OnUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.IsLocked = true;
    }
}
