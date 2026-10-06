using ClassIsland.Core.Attributes;

using ClassIsland.Core.Controls;
namespace ClassIsland.Controls.Components;

/// <summary>
/// StackComponent.xaml 的交互逻辑
/// </summary>
[ContainerComponent]
[ComponentInfo("2D849ECE-9F21-4C78-9434-415CFC283294", "堆叠容器", IconGlyphs.WidgetsOutline, "将多个组件堆叠显示。")]
public partial class StackComponent
{
    public StackComponent()
    {
        InitializeComponent();
    }
}
