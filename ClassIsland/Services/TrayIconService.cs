using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ClassIsland.Controls;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Abstractions.Services.Management;
using ClassIsland.ViewModels;
using ClassIsland.Views;

namespace ClassIsland.Services;

public class TrayIconService
{
    public static readonly ICommand TrayIconLeftClickedCommand = new RoutedCommand();

    private ITaskBarIconService TaskBarIconService { get; }

    private IThemeService ThemeService { get; }

    private IManagementService ManagementService { get; }

    private ILessonsService LessonsService { get; }

    private MainViewModel ViewModel { get; }

    private ClassChangingWindow? _classChangingWindow;

    public TrayIconService(ITaskBarIconService taskBarIconService,
        IThemeService themeService,
        IManagementService managementService,
        ILessonsService lessonsService,
        MainViewModel viewModel)
    {
        TaskBarIconService = taskBarIconService;
        ThemeService = themeService;
        ManagementService = managementService;
        LessonsService = lessonsService;
        ViewModel = viewModel;
    }

    public void Initialize()
    {
        SetupTrayMenu();
        ThemeService.ThemeUpdated += (_, _) => Application.Current?.Dispatcher.BeginInvoke(new Action(SetupTrayMenu));
        TaskBarIconService.MainTaskBarIcon.LeftClickCommand = TrayIconLeftClickedCommand;
        TaskBarIconService.MainTaskBarIcon.TrayLeftMouseUp += MainTaskBarIconOnTrayLeftMouseUp;
    }

    private void SetupTrayMenu()
    {
        // 托盘菜单位于独立的视觉树，收不到应用级主题资源变更通知；主题切换时重建一份以套用新配色。
        var dict = new TrayContextMenu();
        var menu = (ContextMenu)dict["AppContextMenu"];
        menu.DataContext = dict;
        TaskBarIconService.MainTaskBarIcon.DataContext = dict;
        TaskBarIconService.MainTaskBarIcon.ContextMenu = menu;
    }

    private void MainTaskBarIconOnTrayLeftMouseUp(object sender, RoutedEventArgs e)
    {
        switch (ViewModel.Settings.TaskBarIconClickBehavior)
        {
            case 0:
                if (TaskBarIconService.MainTaskBarIcon.ContextMenu != null)
                {
                    GetCursorPos(out var ptr);
                    var dpi = VisualTreeHelper.GetDpi(TaskBarIconService.MainTaskBarIcon.ContextMenu);
                    TaskBarIconService.MainTaskBarIcon.ShowContextMenu(new System.Drawing.Point(
                        (int)(ptr.X / dpi.DpiScaleX), (int)(ptr.Y / dpi.DpiScaleY)));
                }
                break;
            case 1:
                App.GetService<ProfileSettingsWindow>().Open();
                break;
            case 2:
                ViewModel.Settings.IsMainWindowVisible = !ViewModel.Settings.IsMainWindowVisible;
                break;
            case 3:
                OpenClassSwapWindow();
                break;
        }
    }

    public async void OpenClassSwapWindow()
    {
        if (!await ManagementService.AuthorizeByLevel(ManagementService.CredentialConfig.ChangeLessonsAuthorizeLevel))
        {
            return;
        }
        if (LessonsService.CurrentClassPlan == null) // 如果今天没有课程，则选择临时课表
        {
            App.GetService<ProfileSettingsWindow>().OpenDrawer("TemporaryClassPlan");
            App.GetService<ProfileSettingsWindow>().Open();
            return;
        }

        if (_classChangingWindow != null)
        {
            return;
        }

        _classChangingWindow = new ClassChangingWindow()
        {
            ClassPlan = LessonsService.CurrentClassPlan
        };
        _classChangingWindow.ShowDialog();
        _classChangingWindow.DataContext = null;
        _classChangingWindow = null;
    }
}
