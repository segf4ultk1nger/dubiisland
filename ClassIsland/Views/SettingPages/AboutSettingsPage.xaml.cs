using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Controls;
using ClassIsland.Core.Enums.SettingsWindow;

namespace ClassIsland.Views.SettingPages;

/// <summary>
/// AboutSettingsPage.xaml 的交互逻辑
/// </summary>
[SettingsPageInfo("about", "关于 LegacyIsland", IconGlyphs.InfoCircleOutline, IconGlyphs.InfoCircle, SettingsPageCategory.About)]
public partial class AboutSettingsPage : SettingsPageBase
{
    public AboutSettingsPage()
    {
        InitializeComponent();
    }
}
