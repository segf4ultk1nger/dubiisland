using System;
using System.Linq;
using System.Windows;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Controls;
using ClassIsland.Core.Services;
using ClassIsland.Services;
using MahApps.Metro.Controls;
using MahApps.Metro.Controls.Dialogs;

namespace ClassIsland.Controls.Components;

/// <summary>
/// ScheduleComponentSettingsControl.xaml 的交互逻辑
/// </summary>
public partial class ScheduleComponentSettingsControl
{
    public SettingsService SettingsService { get; }

    public ScheduleComponentSettingsControl(SettingsService settingsService)
    {
        SettingsService = settingsService;
        InitializeComponent();
    }

    private async void ButtonImportLegacySettings_OnClick(object sender, RoutedEventArgs e)
    {
        DialogService.TryGetIdentifier(Window.GetWindow(this) as MetroWindow, out var dialogIdentifier);
        var r = await DialogService.ShowMessageAsync(dialogIdentifier, "导入旧版设置",
            "此操作将从 1.4 及以前的课表设置导入课表组件设置，并覆盖当前课表组件的设置。",
            MessageDialogStyle.AffirmativeAndNegative,
            new MetroDialogSettings { AffirmativeButtonText = "继续", NegativeButtonText = "取消" });
        if (r != MessageDialogResult.Affirmative)
        {
            return;
        }
        var settings = SettingsService.Settings;
        Settings.CountdownSeconds = settings.CountdownSeconds;
        Settings.ExtraInfoType = settings.ExtraInfoType;
        Settings.IsCountdownEnabled = settings.IsCountdownEnabled;
        Settings.ShowExtraInfoOnTimePoint = settings.ShowExtraInfoOnTimePoint;
    }

    private void ButtonShowAttachedSettings_OnClick(object sender, RoutedEventArgs e)
    {
        SettingsPageBase.OpenDrawerCommand.Execute(new RootAttachedSettingsDependencyControl(IAttachedSettingsHostService.RegisteredControls.First(x => x.Guid == new Guid("58e5b69a-764a-472b-bcf7-003b6a8c7fdf"))));
    }
}