using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Enums.SettingsWindow;
using ClassIsland.Core.Models.Weather;
using ClassIsland.Core.Services;
using ClassIsland.Models;
using ClassIsland.Services;
using ClassIsland.ViewModels.SettingsPages;
using Microsoft.Extensions.Logging;

using ClassIsland.Core.Controls;
namespace ClassIsland.Views.SettingPages;

/// <summary>
/// WeatherSettingsPage.xaml 的交互逻辑
/// </summary>
[SettingsPageInfo("weather", "天气", IconGlyphs.CloudOutline, IconGlyphs.Cloud, SettingsPageCategory.Internal)]
public partial class WeatherSettingsPage : SettingsPageBase
{
    public WeatherSettingsViewModel ViewModel { get; } = new();

    public SettingsService SettingsService { get; }

    public IWeatherService WeatherService { get; }
    public ILocationService LocationService { get; }
    public ILogger<WeatherSettingsPage> Logger { get; }

    // [搜索城市或地区] TextBox的全局变量 用于防抖
    private TextBox GlobalTextBoxSearchCity { get; set; } = null!;

    // [搜索城市或地区] 防抖定时器
    private DispatcherTimer SearchDebounceTimer { get; set; } = null!;

    public WeatherSettingsPage(SettingsService settingsService, IWeatherService weatherService, ILocationService locationService, ILogger<WeatherSettingsPage> logger)
    {
        InitializeComponent();
        DataContext = this;
        WeatherService = weatherService;
        LocationService = locationService;
        Logger = logger;
        SettingsService = settingsService;
        SettingsService.Settings.PropertyChanged += SettingsOnPropertyChanged;
        // [搜索城市或地区] 初始化防抖定时器
        Loaded += WeatherSettingsPage_Loaded;
    }

    private void SettingsOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Settings.LastWeatherInfo))
        {
            ViewModel.BuildFrom(SettingsService.Settings.LastWeatherInfo, WeatherService,
                SettingsService.Settings.ExcludedWeatherAlerts);
        }
    }

    private async void ButtonRefreshWeather_OnClick(object sender, RoutedEventArgs e)
    {
        await WeatherService.QueryWeatherAsync();
    }

    private void ButtonEditCurrentCity_OnClick(object sender, RoutedEventArgs e)
    {
        OpenDrawer("CitySearcher");
    }

    private void TextBoxExcludedAlerts_OnLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        }

        ViewModel.RefreshAlerts(SettingsService.Settings.ExcludedWeatherAlerts);
    }

    /// <summary>
    /// [搜索城市或地区] 防抖定时器初始化
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private async void WeatherSettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        // 初始化防抖定时器，设置间隔时间为50毫秒
        SearchDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        SearchDebounceTimer.Tick += SearchDebounceTimer_Tick;
        SearchDebounceTimer.Stop();

        ViewModel.BuildFrom(SettingsService.Settings.LastWeatherInfo, WeatherService,
            SettingsService.Settings.ExcludedWeatherAlerts);
        ViewModel.CitySearchResults = await WeatherService.GetCitiesByName(string.Empty);
    }

    /// <summary>
    /// [搜索城市或地区] TextBox的文本改变事件
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void TextBoxSearchCity_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        // 为全局变量赋值
        GlobalTextBoxSearchCity = (TextBox)sender;

        // 重置定时器
        SearchDebounceTimer.Stop();
        SearchDebounceTimer.Start();
    }

    /// <summary>
    /// [搜索城市或地区] 防抖定时器触发事件
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private async void SearchDebounceTimer_Tick(object? sender, EventArgs e)
    {
        // 停止定时器
        SearchDebounceTimer.Stop();

        // 更新搜索结果
        ViewModel.IsSearchingWeather = true;
        var searchText = GlobalTextBoxSearchCity.Text;
        ViewModel.CitySearchResults =
            await WeatherService.GetCitiesByName(searchText);
        ViewModel.IsSearchingWeather = false;
    }

    private async void SelectorCity_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var listbox = (ListBox)sender;
        var city = (City?)listbox.SelectedItem;
        if (city == null)
        {
            e.Handled = true;
            //Settings.CityName = "";
            return;
        }
        SettingsService.Settings.CityName = city.Name;
        await WeatherService.QueryWeatherAsync();
    }

    private async void ButtonGetCurrentPos_OnClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsLocating)
        {
            return;
        }

        ViewModel.IsLocating = true;
        try
        {
            var pos = await LocationService.GetLocationAsync();
            SettingsService.Settings.WeatherLongitude = Math.Round(pos.Longitude, 4);
            SettingsService.Settings.WeatherLatitude = Math.Round(pos.Latitude, 4);
            await WeatherService.QueryWeatherAsync();
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "无法获取当前位置");
            ViewModel.IsLocating = false;
            await DialogService.ShowMessageAsync(SettingsPageBase.DialogHostIdentifier, "获取当前位置失败",
                "无法获取当前位置。请确认系统的定位服务已开启，并已允许 LegacyIsland 访问位置。");
        }
        finally
        {
            ViewModel.IsLocating = false;
        }
    }

    private double _fakeScrollDragStartOffset;
    private Point _fakeScrollDragStartPoint;

    private void RootScroll_OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        UpdateFakeScrollBar();
    }

    private void RootScroll_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateFakeScrollBar();
    }

    private void FakeScrollBar_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateFakeScrollBar();
    }

    /// <summary>
    /// 根据滚动进度更新假滚动条滑块的位置与大小；无需滚动时隐藏。
    /// </summary>
    private void UpdateFakeScrollBar()
    {
        if (FakeScrollBar == null || FakeScrollThumb == null || RootScroll == null)
        {
            return;
        }

        var trackHeight = FakeScrollBar.ActualHeight;
        if (trackHeight <= 0)
        {
            return;
        }

        // 自定义模板下若 ExtentHeight 未上报，则回退用内容实际高度。
        var extent = RootScroll.ExtentHeight;
        if (extent <= 0 && RootScroll.Content is FrameworkElement content)
        {
            extent = content.ActualHeight;
        }

        var viewport = RootScroll.ViewportHeight > 0 ? RootScroll.ViewportHeight : RootScroll.ActualHeight;
        var scrollable = extent - viewport;
        if (scrollable <= 0.5)
        {
            // 内容不足一屏，无需滚动。
            FakeScrollBar.Visibility = Visibility.Collapsed;
            return;
        }

        FakeScrollBar.Visibility = Visibility.Visible;
        var thumbHeight = Math.Max(28, trackHeight * viewport / extent);
        var maxTop = trackHeight - thumbHeight;
        var top = maxTop * (RootScroll.VerticalOffset / scrollable);
        FakeScrollThumb.Height = thumbHeight;
        FakeScrollThumb.Margin = new Thickness(0, top, 2, 0);
    }

    private void FakeScrollThumb_OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        _fakeScrollDragStartPoint = e.GetPosition(FakeScrollBar);
        _fakeScrollDragStartOffset = RootScroll.VerticalOffset;
        FakeScrollThumb.CaptureMouse();
        e.Handled = true;
    }

    private void FakeScrollThumb_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!FakeScrollThumb.IsMouseCaptured)
        {
            return;
        }

        var trackHeight = FakeScrollBar.ActualHeight;
        var maxTop = trackHeight - FakeScrollThumb.ActualHeight;
        var maxOffset = RootScroll.ExtentHeight - RootScroll.ViewportHeight;
        if (maxTop <= 0 || maxOffset <= 0)
        {
            return;
        }

        var delta = e.GetPosition(FakeScrollBar).Y - _fakeScrollDragStartPoint.Y;
        RootScroll.ScrollToVerticalOffset(_fakeScrollDragStartOffset + delta / maxTop * maxOffset);
        e.Handled = true;
    }

    private void FakeScrollThumb_OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        FakeScrollThumb.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void ForecastHost_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        ViewModel.ApplyWidth(e.NewSize.Width);
    }
}