using System;
using System.Diagnostics;
using System.Windows;
using ClassIsland.Core;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Abstractions.Services.Management;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Enums.SettingsWindow;
using ClassIsland.Core.Services;
using ClassIsland.Models;
using ClassIsland.Services;
using ClassIsland.ViewModels.SettingsPages;
using MahApps.Metro.Controls.Dialogs;
using Microsoft.Extensions.Logging;
using CommonDialog = ClassIsland.Core.Controls.CommonDialog.CommonDialog;
using Path = System.IO.Path;

using ClassIsland.Core.Controls;
namespace ClassIsland.Views.SettingPages;

/// <summary>
/// StorageSettingsPage.xaml 的交互逻辑
/// </summary>
[SettingsPageInfo("storage", "存储", IconGlyphs.DatabaseOutline, IconGlyphs.Database, SettingsPageCategory.Internal)]
public partial class StorageSettingsPage
{
    public StorageSettingsViewModel ViewModel { get; } = new();

    public FileFolderService FileFolderService { get; }
    public SettingsService SettingsService { get; }
    public ILogger<StorageSettingsPage> Logger { get; }
    public IManagementService ManagementService { get; }

    public StorageSettingsPage(FileFolderService fileFolderService, SettingsService settingsService, ILogger<StorageSettingsPage> logger, IManagementService managementService)
    {
        FileFolderService = fileFolderService;
        SettingsService = settingsService;
        Logger = logger;
        ManagementService = managementService;
        DataContext = this;
        InitializeComponent();
        _ = ViewModel.RefreshStorageInfoAsync();
    }

    private void ButtonRefreshStorage_OnClick(object sender, RoutedEventArgs e)
    {
        _ = ViewModel.RefreshStorageInfoAsync();
    }

    private async void ButtonCreateBackup_OnClick(object sender, RoutedEventArgs e)
    {
        ViewModel.IsBackupFinished = false;
        ViewModel.IsBackingUp = true;
        try
        {
            await FileFolderService.CreateBackupAsync();
            ViewModel.IsBackupFinished = true;
            _ = ViewModel.RefreshStorageInfoAsync();
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "无法创建备份。");
            CommonDialog.ShowError($"无法创建备份：{exception.Message}");
        }
        ViewModel.IsBackingUp = false;
    }

    private void ButtonViewBackupFiles_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo()
            {
                FileName = System.IO.Path.GetFullPath(Path.Combine(App.AppRootFolderPath, "Backups")),
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "无法浏览备份文件。");
            CommonDialog.ShowError($"无法浏览备份文件：{exception.Message}");
        }
    }

    private async void ButtonRecoverBackup_OnClick(object sender, RoutedEventArgs e)
    {
        if (!await ManagementService.AuthorizeByLevel(ManagementService.CredentialConfig.ExitApplicationAuthorizeLevel))
        {
            return;
        }
        AppBase.Current.Restart(["-m", "-r"]);
    }

    private void ButtonOpenFolder_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string path })
        {
            return;
        }

        try
        {
            System.IO.Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "无法打开目录 {Path}", path);
            CommonDialog.ShowError($"无法打开目录：{exception.Message}");
        }
    }

    private async void ButtonCleanupSelected_OnClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.CleanupSelectedAsync();
    }

    private async void ButtonResetSettings_OnClick(object sender, RoutedEventArgs e)
    {
        if (!await ManagementService.AuthorizeByLevel(ManagementService.CredentialConfig.ExitApplicationAuthorizeLevel))
        {
            return;
        }

        var result = await DialogService.ShowMessageAsync(SettingsPageBase.DialogHostIdentifier,
            "重置所有设置", "将把所有设置恢复为默认值，且不会删除档案和备份。重置后应用将重启。是否继续？",
            MessageDialogStyle.AffirmativeAndNegative,
            new MetroDialogSettings
            {
                AffirmativeButtonText = "重置并重启",
                NegativeButtonText = "取消",
                DefaultButtonFocus = MessageDialogResult.Negative
            });
        if (result != MessageDialogResult.Affirmative)
        {
            return;
        }

        var lastVersion = SettingsService.Settings.LastAppVersion;
        SettingsService.Settings = new Settings { LastAppVersion = lastVersion };
        SettingsService.SaveSettings("重置设置");
        RequestRestart();
    }
}