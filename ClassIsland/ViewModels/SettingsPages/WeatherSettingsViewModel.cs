using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Media;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Models.Weather;
using ClassIsland.Helpers;
using ClassIsland.Models.Weather;
using CommunityToolkit.Mvvm.ComponentModel;
using MahApps.Metro.IconPacks;

namespace ClassIsland.ViewModels.SettingsPages;

public partial class WeatherSettingsViewModel : ObservableRecipient
{
    private const double DailyItemMinWidth = 100;
    private const double HourlyItemMinWidth = 64;

    private List<City> _citySearchResults = new();
    private List<DailyForecastItem> _allDaily = new();
    private List<HourlyForecastItem> _allHourly = new();
    private IReadOnlyList<WeatherAlert> _allAlerts = new List<WeatherAlert>();
    private IReadOnlyList<string> _alertExclusions = new List<string>();
    private double _lastWidth = 1000;

    [ObservableProperty] private bool _isSearchingWeather;
    [ObservableProperty] private bool _isLocating;

    [ObservableProperty] private PackIconRemixIconKind _currentIcon = PackIconRemixIconKind.CloudyFill;
    [ObservableProperty] private string _currentWeatherText = "";
    [ObservableProperty] private string _uvText = "";
    [ObservableProperty] private string _updateTimeText = "";
    [ObservableProperty] private Brush? _heroBackground;
    [ObservableProperty] private int _selectedTab;

    [ObservableProperty] private ObservableCollection<DailyForecastItem> _visibleDaily = [];
    [ObservableProperty] private ObservableCollection<HourlyForecastItem> _visibleHourly = [];

    [ObservableProperty] private ObservableCollection<WeatherAlertItem> _alerts = [];
    [ObservableProperty] private bool _hasAlerts;

    public List<City> CitySearchResults
    {
        get => _citySearchResults;
        set
        {
            if (Equals(value, _citySearchResults)) return;
            _citySearchResults = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 根据可用宽度决定每日/逐小时各显示多少项，并等分铺满。
    /// </summary>
    public void ApplyWidth(double width)
    {
        if (width > 0)
        {
            _lastWidth = width;
        }

        var dailyCount = _allDaily.Count == 0
            ? 0
            : Math.Max(1, Math.Min(_allDaily.Count, (int)(_lastWidth / DailyItemMinWidth)));
        VisibleDaily = new ObservableCollection<DailyForecastItem>(_allDaily.Take(dailyCount));

        var hourlyCount = _allHourly.Count == 0
            ? 0
            : Math.Max(1, Math.Min(_allHourly.Count, (int)(_lastWidth / HourlyItemMinWidth)));
        VisibleHourly = new ObservableCollection<HourlyForecastItem>(_allHourly.Take(hourlyCount));
    }

    /// <summary>
    /// 将天气数据摊平成 hero 与各 Tab 需要的展示字段。
    /// </summary>
    public void BuildFrom(WeatherInfo info, IWeatherService weatherService, IReadOnlyList<string> excludedAlerts)
    {
        if (info == null)
        {
            return;
        }

        _allAlerts = weatherService.AllAlerts.Count > 0 ? weatherService.AllAlerts : info.Alerts;
        _alertExclusions = excludedAlerts;

        var current = info.Current;
        var suns = info.ForecastDaily.SunRiseSet.Value;
        var sunrise = suns.Count > 0 ? ParseDate(suns[0].From) : null;
        var sunset = suns.Count > 0 ? ParseDate(suns[0].To) : null;
        var now = current.PublishTime == default ? DateTime.Now : current.PublishTime;
        var isNight = sunrise != null && sunset != null
            ? now < sunrise || now >= sunset
            : now.Hour < 6 || now.Hour >= 19;

        CurrentIcon = WeatherIconHelper.GetIcon(current.Weather, isNight);
        CurrentWeatherText = weatherService.GetWeatherTextByCode(current.Weather);
        HeroBackground = WeatherIconHelper.GetHeroBackground(current.Weather, isNight);
        UvText = WeatherIconHelper.GetUvText(current.UvIndex);
        UpdateTimeText = info.UpdateTime.ToString("HH:mm");
        BuildAlerts();

        // 逐日
        var dailyTemps = info.ForecastDaily.Temperature.Value;
        var dailyWeather = info.ForecastDaily.Weather.Value;
        var precips = info.ForecastDaily.PrecipitationProbability.Value;
        var startDate = sunrise?.Date ?? DateTime.Today;
        var daily = new List<DailyForecastItem>();
        for (var i = 0; i < dailyTemps.Count; i++)
        {
            var date = i < suns.Count && ParseDate(suns[i].From) is { } d ? d.Date : startDate.AddDays(i);
            var code = i < dailyWeather.Count ? dailyWeather[i].From : "";
            daily.Add(new DailyForecastItem
            {
                DayText = i == 0 ? "今天" : date.ToString("ddd", CultureInfo.GetCultureInfo("zh-CN")),
                DateText = date.ToString("M/d"),
                Icon = WeatherIconHelper.GetIcon(code),
                High = $"{dailyTemps[i].From}°",
                Low = $"{dailyTemps[i].To}°",
                ConditionText = weatherService.GetWeatherTextByCode(code),
                HasPrecip = i < precips.Count && !string.IsNullOrEmpty(precips[i]),
                PrecipProbability = i < precips.Count ? $"{precips[i]}%" : "",
            });
        }

        _allDaily = daily;

        // 逐小时
        var hourlyTemps = info.ForecastHourly.Temperature.Value;
        var hourlyWeather = info.ForecastHourly.Weather.Value;
        var hourlyStart = info.ForecastHourly.Temperature.PubTime ?? DateTime.Now;
        var hourly = new List<HourlyForecastItem>();
        for (var i = 0; i < hourlyTemps.Count; i++)
        {
            var t = hourlyStart.AddHours(i);
            var code = i < hourlyWeather.Count ? hourlyWeather[i].ToString() : "";
            hourly.Add(new HourlyForecastItem
            {
                TimeText = i == 0 ? "现在" : t.ToString("HH:mm"),
                Icon = WeatherIconHelper.GetIcon(code, t.Hour < 6 || t.Hour >= 19),
                Temperature = $"{hourlyTemps[i]}°",
            });
        }

        _allHourly = hourly;
        ApplyWidth(_lastWidth);
    }

    /// <summary>
    /// 按当前排除规则重新计算列表的排除状态，不重新请求数据。
    /// </summary>
    public void RefreshAlerts(IReadOnlyList<string> excludedAlerts)
    {
        _alertExclusions = excludedAlerts;
        BuildAlerts();
    }

    private void BuildAlerts()
    {
        var gray = Color.FromRgb(0x9C, 0xA3, 0xAF);
        var items = new List<WeatherAlertItem>();
        foreach (var alert in _allAlerts)
        {
            var excluded = _alertExclusions.FirstOrDefault(x =>
                !string.IsNullOrWhiteSpace(x) && alert.Title.Contains(x)) != null;
            var color = excluded ? gray : WeatherAlertHelper.GetLevelColor(alert.Level);
            items.Add(new WeatherAlertItem
            {
                Title = alert.Title,
                Type = alert.Type,
                LevelText = excluded ? "已排除" : alert.Level,
                Detail = alert.Detail,
                TimeText = alert.PubTime.ToString("HH:mm"),
                IsExcluded = excluded,
                Icon = WeatherAlertHelper.GetTypeIcon(alert.Type),
                LevelBrush = new SolidColorBrush(color),
                LevelTint = new SolidColorBrush(Color.FromArgb(0x1F, color.R, color.G, color.B)),
            });
        }

        Alerts = new ObservableCollection<WeatherAlertItem>(items);
        HasAlerts = Alerts.Count > 0;
    }

    private static DateTime? ParseDate(string value) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var result)
            ? result
            : null;
}
