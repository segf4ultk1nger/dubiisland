using System.Windows;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Services;
using ClassIsland.Views;
using MahApps.Metro.Controls;

namespace ClassIsland.Controls.ActionSettingsControls;

/// <summary>
/// NotificationActionSettingsControl.xaml 的交互逻辑
/// </summary>
public partial class NotificationActionSettingsControl
{
    public static readonly DependencyProperty IsShowInDialogProperty = DependencyProperty.Register(
        nameof(IsShowInDialog), typeof(bool), typeof(NotificationActionSettingsControl), new PropertyMetadata(default(bool)));

    public bool IsShowInDialog
    {
        get { return (bool)GetValue(IsShowInDialogProperty); }
        set { SetValue(IsShowInDialogProperty, value); }
    }

    public NotificationActionSettingsControl()
    {
        InitializeComponent();
    }

    private void ButtonShowSettings_OnClick(object sender, RoutedEventArgs e)
    {
        if (FindResource("SettingsDrawer") is not FrameworkElement drawer)
        {
            return;
        }

        drawer.DataContext = this;
        if (Window.GetWindow(this) is SettingsWindowNew)  // 在应用设置中展示
        {
            IsShowInDialog = false;
            SettingsPageBase.OpenDrawerCommand.Execute(drawer);
        }
        else
        {
            IsShowInDialog = true;
            if (Window.GetWindow(this) is MetroWindow window && DialogService.TryGetIdentifier(window, out var identifier))
                _ = DialogService.ShowAsync(drawer, identifier);
        }
    }
}