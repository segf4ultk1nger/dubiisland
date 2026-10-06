using System.Windows;
using System.Windows.Data;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Controls.Scripting;

namespace ClassIsland.Controls.Components;

/// <summary>
/// SlideComponentSettingsControl.xaml 的交互逻辑
/// </summary>
public partial class SlideComponentSettingsControl
{
    private void OpenDrawer(string key)
    {
        if (FindResource(key) is not FrameworkElement drawer)
        {
            return;
        }

        drawer.DataContext = this;
        SettingsPageBase.OpenDrawerCommand.Execute(drawer);
    }

    public SlideComponentSettingsControl()
    {
        InitializeComponent();
    }

    private void ButtonOpenPauseCondition_OnClick(object sender, RoutedEventArgs e)
    {
        OpenConditionEditor(nameof(Settings.PauseCondition));
    }

    private void ButtonOpenStopCondition_OnClick(object sender, RoutedEventArgs e)
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
}