#if DEBUG
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ClassIsland.Core.Models.Weather;

namespace ClassIsland.Helpers;

/// <summary>
/// 仅 Debug 可用的天气 mock：城市统一叫「春田镇」，每次刷新随机生成一份天气数据，
/// 用于测试各种天气状况、预警、降水与空气质量展示。通过在城市搜索输入 "mock" 触发。
/// </summary>
internal static class WeatherMock
{
    private const string Prefix = "mock:";

    private static readonly Random Rng = new();

    private static readonly (string Spec, string Label)[] Scenarios =
    {
        ("mock:random", "随机天气"),
        ("mock:0", "晴"),
        ("mock:1", "多云"),
        ("mock:4", "雷阵雨"),
        ("mock:13", "阵雪"),
        ("mock:53", "霾"),
        ("mock:night", "夜间"),
        ("mock:alert", "气象预警"),
        ("mock:aqi", "重污染"),
    };

    private static readonly int[] CommonCodes = { 0, 1, 2, 3, 4, 7, 8, 13, 14, 29, 30, 32, 53 };

    private static readonly (string Icon, string Type, string Level, string Title)[] AlertLevels =
    {
        ("http://f5.market.xiaomi.com/download/Weather/0ac110d2ee20a454ab44f5df30f9fa6ff650e0b72/a.webp", "大风", "蓝色", "气象台发布大风蓝色预警"),
        ("http://f4.market.mi-img.com/download/Weather/072013febeb1944da85649e5e547ec5a8284816a2/a.webp", "暴雨", "黄色", "气象台发布暴雨黄色预警"),
        ("http://f5.market.xiaomi.com/download/Weather/06db501333e6d4075a3364a66cdf23ba5733111b3/a.webp", "雷电", "橙色", "气象台发布雷电橙色预警"),
        ("http://f3.market.xiaomi.com/download/Weather/03e3e096d3d9e485fa33bbf833fc3b3c96c23d014/a.webp", "高温", "红色", "气象台发布高温红色预警"),
    };

    public static bool IsMock(string? cityId) =>
        !string.IsNullOrEmpty(cityId) && cityId.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);

    public static bool Matches(string? query) =>
        (query ?? "").Trim().StartsWith("mock", StringComparison.OrdinalIgnoreCase);

    public static List<City> GetCities(string? query)
    {
        query = (query ?? "").Trim();
        if (query.Length > Prefix.Length)
        {
            return new List<City>
            {
                new() { Name = "春田镇", Region = "自定义天气代码 " + query[Prefix.Length..], CityId = query.ToLowerInvariant() }
            };
        }

        return Scenarios
            .Select(s => new City { Name = "春田镇", Region = s.Label, CityId = s.Spec })
            .ToList();
    }

    public static WeatherInfo Create(string cityId)
    {
        var body = cityId.Length > Prefix.Length ? cityId[Prefix.Length..].ToLowerInvariant() : "random";
        var fixedCode = int.TryParse(body, out var parsed) ? parsed : (int?)null;
        var wantNight = body == "night";
        var wantAlert = body == "alert";
        var wantAqi = body == "aqi";

        var code = fixedCode ?? CommonCodes[Rng.Next(CommonCodes.Length)];
        var now = DateTime.Now;
        var publish = wantNight ? now.Date.AddHours(23).AddMinutes(37) : now.Date.AddHours(12);

        var temperature = code is 13 or 14 or 15 or 16 or 17 or 26 or 27 or 28 or 34
            ? Rng.Next(-12, 3)
            : Rng.Next(2, 36);
        var high = temperature + Rng.Next(3, 8);
        var low = temperature - Rng.Next(3, 10);
        var uv = Rng.Next(0, 12);
        var aqi = wantAqi ? Rng.Next(210, 330) : Rng.Next(18, 130);

        var info = new WeatherInfo
        {
            UpdateTimeUnix = DateTimeOffset.Now.ToUnixTimeMilliseconds(),
            Current = new CurrentWeather
            {
                Temperature = new ValueUnitPair { Value = temperature.ToString(), Unit = "℃" },
                FeelsLike = new ValueUnitPair { Value = (temperature + Rng.Next(-3, 4)).ToString(), Unit = "℃" },
                Humidity = new ValueUnitPair { Value = Rng.Next(30, 96).ToString(), Unit = "%" },
                Pressure = new ValueUnitPair { Value = Rng.Next(985, 1035).ToString(), Unit = "hPa" },
                Visibility = new ValueUnitPair { Value = Rng.Next(1, 31).ToString(), Unit = "km" },
                UvIndex = uv.ToString(),
                Weather = code.ToString(),
                PublishTime = publish,
                Wind = new WindInfo
                {
                    Direction = new ValueUnitPair { Value = new[] { "东北", "东", "东南", "南", "西南", "西", "西北", "北" }[Rng.Next(8)] },
                    Speed = new ValueUnitPair { Value = Rng.Next(1, 31).ToString(), Unit = "km/h" },
                },
            },
            Aqi = new AqiInfo
            {
                Aqi = aqi.ToString(),
                Pm25 = Rng.Next(10, 300).ToString(),
                Pm10 = Rng.Next(10, 300).ToString(),
                No2 = Rng.Next(5, 120).ToString(),
                So2 = Rng.Next(2, 60).ToString(),
                Co = (Rng.Next(2, 20) / 10.0).ToString(CultureInfo.InvariantCulture),
                O3 = Rng.Next(20, 200).ToString(),
                Primary = "pm25",
                Source = "小米天气 (Mock)",
                Suggest = aqi > 200 ? "空气重度污染，建议减少户外活动并佩戴口罩。" : "空气一般，敏感人群注意防护。",
            },
            Indices = new WeatherIndices
            {
                Items =
                {
                    new WeatherIndexItem { Type = "uvIndex", Value = uv.ToString() },
                    new WeatherIndexItem { Type = "sports", Value = "较适宜" },
                    new WeatherIndexItem { Type = "carWash", Value = "不宜" },
                },
            },
            Yesterday = new YesterdayWeather
            {
                Date = now.Date.AddDays(-1),
                SunRise = now.Date.AddDays(-1).AddHours(6),
                SunSet = now.Date.AddDays(-1).AddHours(18),
                TempMax = high.ToString(),
                TempMin = low.ToString(),
                Aqi = aqi.ToString(),
                WeatherStart = code.ToString(),
                WeatherEnd = code.ToString(),
            },
        };

        BuildDaily(info, code, high, low, now, fixedCode != null);
        BuildHourly(info, code, temperature, now, fixedCode != null);
        BuildMinutely(info, code);
        if (wantAlert)
        {
            BuildAlerts(info, cityId);
        }

        return info;
    }

    private static void BuildDaily(WeatherInfo info, int code, int high, int low, DateTime now, bool fixedCode)
    {
        info.ForecastDaily.Temperature.Value = new();
        info.ForecastDaily.Weather.Value = new();
        info.ForecastDaily.PrecipitationProbability.Value = new();
        info.ForecastDaily.Aqi.Value = new();
        info.ForecastDaily.SunRiseSet.Value = new();
        for (var i = 0; i < 15; i++)
        {
            var date = now.Date.AddDays(i);
            var h = high + Rng.Next(-4, 5);
            var l = low + Rng.Next(-4, 5);
            if (l >= h)
            {
                l = h - 1;
            }

            var c = fixedCode ? code : CommonCodes[Rng.Next(CommonCodes.Length)];
            info.ForecastDaily.Temperature.Value.Add(new RangedValue { From = h.ToString(), To = l.ToString() });
            info.ForecastDaily.Weather.Value.Add(new RangedValue { From = c.ToString(), To = c.ToString() });
            info.ForecastDaily.PrecipitationProbability.Value.Add(Rng.Next(0, 100).ToString());
            info.ForecastDaily.Aqi.Value.Add(Rng.Next(20, 160));
            info.ForecastDaily.SunRiseSet.Value.Add(new RangedValue
            {
                From = date.AddHours(5).AddMinutes(Rng.Next(0, 90)).ToString("o"),
                To = date.AddHours(18).AddMinutes(Rng.Next(0, 90)).ToString("o"),
            });
        }
    }

    private static void BuildHourly(WeatherInfo info, int code, int current, DateTime now, bool fixedCode)
    {
        info.ForecastHourly.Temperature.Value = new();
        info.ForecastHourly.Weather.Value = new();
        info.ForecastHourly.Aqi.Value = new();
        info.ForecastHourly.Temperature.PubTime = now.Date.AddHours(now.Hour);
        for (var i = 0; i < 24; i++)
        {
            var c = fixedCode ? code : CommonCodes[Rng.Next(CommonCodes.Length)];
            info.ForecastHourly.Temperature.Value.Add(current + Rng.Next(-4, 5));
            info.ForecastHourly.Weather.Value.Add(c);
            info.ForecastHourly.Aqi.Value.Add(Rng.Next(20, 160));
        }
    }

    private static void BuildMinutely(WeatherInfo info, int code)
    {
        if (code is not (3 or 4 or 7 or 8 or 9 or 10 or 11 or 12 or 19))
        {
            return;
        }

        var value = new List<double>();
        for (var i = 0; i < 60; i++)
        {
            value.Add(i < 30 ? 0.5 : 0.0);
        }

        info.Minutely.Precipitation.Value = value;
    }

    private static void BuildAlerts(WeatherInfo info, string cityId)
    {
        for (var i = 0; i < AlertLevels.Length; i++)
        {
            var (icon, type, level, title) = AlertLevels[i];
            info.Alerts.Add(new WeatherAlert
            {
                LocationKey = cityId,
                AlertId = "mock-alert-" + i,
                PubTime = DateTime.Now.AddMinutes(-i * 7),
                Title = title,
                Type = type,
                Level = level,
                Detail = $"{title}：受强对流天气影响，预计未来 6 小时内本市将出现{type}天气，请注意防范。（Mock {i + 1}/{AlertLevels.Length}）",
                Images = new Dictionary<string, string> { ["icon"] = icon },
            });
        }
    }
}
#endif
