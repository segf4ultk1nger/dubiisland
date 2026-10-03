using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ClassIsland.Core.Abstractions.Services;

namespace ClassIsland.Controls;

/// <summary>
/// WelcomeWindowIntroControl.xaml 的交互逻辑
/// </summary>
public partial class WelcomeWindowIntroControl : UserControl
{
    private IHangService HangService { get; } = App.GetService<IHangService>();


    public WelcomeWindowIntroControl()
    {
        InitializeComponent();
    }

    protected override void OnInitialized(EventArgs e)
    {
        var theme = App.GetService<IThemeService>();
        var foreground = theme.CurrentRealThemeMode == 0
            ? Color.FromRgb(33, 33, 33)
            : Color.FromRgb(255, 255, 255);
        Foreground = new SolidColorBrush(foreground);
        _ = Task.Run(() =>
        {
            Play("Intro");
            Thread.Sleep(4000);
            HangService.AssumeHang();
            while (HangService.IsHang)
            {
            }
            Play("Outro");
        });
        base.OnInitialized(e);
    }

    private void Play(string key)
    {
        Dispatcher.Invoke(() =>
        {
            BeginStoryboard((Storyboard)FindResource(key));
        });
    }
}