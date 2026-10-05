using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using ClassIsland.Helpers;
using ClassIsland.Services;
using ClassIsland.Shared;
using ClassIsland.Shared.Helpers;

namespace ClassIsland.Controls;

public partial class PrivilegedAutoStartButton : UserControl
{
    private bool _busy;

    public PrivilegedAutoStartButton()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        ShieldIcon.Source = GetShieldIconSource();
        await RefreshAsync();
    }

    private static ImageSource? GetShieldIconSource()
    {
        try
        {
            var source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                System.Drawing.SystemIcons.Shield.Handle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch
        {
            return null;
        }
    }

    private async Task RefreshAsync()
    {
        var isSet = await Task.Run(ScheduledTaskHelper.IsRegistered);
        Label.Text = isSet ? "移除提权启动" : "设置提权启动";
    }

    private async void Button_OnClick(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }
        _busy = true;

        ActionButton.IsEnabled = false;
        ShieldIcon.Visibility = Visibility.Collapsed;
        Spinner.Visibility = Visibility.Visible;
        SpinnerRotate.BeginAnimation(RotateTransform.AngleProperty,
            new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1)) { RepeatBehavior = RepeatBehavior.Forever });
        Label.Text = "设置中...";

        var wantSet = !await Task.Run(ScheduledTaskHelper.IsRegistered);
        var exe = FrameworkCompat.ProcessPath.Replace(".dll", ".exe");
        var ok = await Task.Run(() => wantSet
            ? ScheduledTaskHelper.Register(exe, "-m")
            : ScheduledTaskHelper.Unregister());
        if (wantSet && ok)
        {
            // 与普通开机自启互斥，避免开机双启。
            IAppHost.GetService<SettingsService>().Settings.IsAutoStartEnabled = false;
        }

        SpinnerRotate.BeginAnimation(RotateTransform.AngleProperty, null);
        Spinner.Visibility = Visibility.Collapsed;
        ShieldIcon.Visibility = Visibility.Visible;
        ActionButton.IsEnabled = true;
        _busy = false;
        await RefreshAsync();
    }
}
