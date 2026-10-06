using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Controls.Scripting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using ClassIsland.Services;

namespace ClassIsland.Controls.Components;

/// <summary>
/// RollingComponentSettingsControl.xaml 的交互逻辑
/// </summary>
public partial class RollingComponentSettingsControl
{
    public SettingsService SettingsService { get; }

    public RollingComponentSettingsControl(SettingsService settingsService)
    {
        SettingsService = settingsService;
        InitializeComponent();
    }

    private void OpenDrawer(string key)
    {
        if (FindResource(key) is not FrameworkElement drawer)
        {
            return;
        }

        drawer.DataContext = this;
        SettingsPageBase.OpenDrawerCommand.Execute(drawer);
    }

    private void ButtonOpenPauseRuleset_OnClick(object sender, RoutedEventArgs e)
    {
        OpenConditionEditor(nameof(Settings.PauseCondition));
    }

    private void ButtonOpenStopRuleset_OnClick(object sender, RoutedEventArgs e)
    {
        OpenConditionEditor(nameof(Settings.StopCondition));
    }

    private void OpenConditionEditor(string propertyName)
    {
        if (FindResource("ConditionEditor") is ConditionEditor editor)
        {
            editor.SetBinding(ConditionEditor.ConditionProperty,
                new Binding(propertyName) { Source = Settings, Mode = BindingMode.TwoWay });
        }

        OpenDrawer("ConditionEditor");
    }

    private void ButtonCloseWarningTip_OnClick(object sender, RoutedEventArgs e)
    {
        SettingsService.Settings.IsRollingComponentWarningVisible = false;
    }
}