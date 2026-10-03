using System.Windows;
using System.Windows.Controls;

namespace ClassIsland.Controls.Demo;

/// <summary>
/// ThemeDemo.xaml 的交互逻辑
/// </summary>
public partial class ThemeDemo : UserControl
{
    public static readonly DependencyProperty ThemeModeProperty = DependencyProperty.Register(
        nameof(ThemeMode), typeof(string), typeof(ThemeDemo), new PropertyMetadata("Light"));

    public string ThemeMode
    {
        get { return (string)GetValue(ThemeModeProperty); }
        set { SetValue(ThemeModeProperty, value); }
    }

    public ThemeDemo()
    {
        InitializeComponent();
    }
}
