using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using ClassIsland.Core.Models.Components;
using ClassIsland.Core.Models.Weather;
using ClassIsland.Models.ComponentSettings;
using MahApps.Metro.IconPacks;

namespace ClassIsland.Controls.Island.Components;

/// <summary>
/// 天气简报组件（等价 WeatherComponent.xaml）。用 RemixIcon 近似替代原天气图标模板。
/// </summary>
public sealed class WeatherIslandComponent : IslandComponentBase
{
    private const double Gap = 4;
    private const double IconSize = 22;

    public WeatherIslandComponent(ComponentSettings component) : base(component)
    {
    }

    private WeatherComponentSettings Settings =>
        IslandComponentFactory.GetSettings<WeatherComponentSettings>(Component);

    protected override Size MeasureContent(Size availableSize, IslandContext context)
    {
        var parts = BuildParts();
        var width = 0.0;
        var height = 40.0;
        foreach (var part in parts)
        {
            width += part.Width + Gap;
            height = Math.Max(height, part.Height);
        }

        return new Size(width, height);
    }

    protected override void RenderContent(DrawingContext drawingContext, Rect slot, IslandContext context)
    {
        var parts = BuildParts();
        var x = slot.X;
        foreach (var part in parts)
        {
            drawingContext.DrawText(part, new Point(x, slot.Y + (slot.Height - part.Height) / 2));
            x += part.Width + Gap;
        }
    }

    private List<FormattedText> BuildParts()
    {
        var parts = new List<FormattedText>();
        var info = Context.Settings.LastWeatherInfo;
        if (info == null)
            return parts;

        var settings = Settings;
        if (settings.ShowMainWeatherInfo)
        {
            switch (settings.MainWeatherInfoKind)
            {
                case 0:
                    parts.Add(WeatherIcon(info.Current.Weather));
                    parts.Add(MakeText(info.Current.Temperature.ToString(), BodyFontSize, ForegroundBrush));
                    break;
                case 1:
                    parts.Add(Icon(PackIconRemixIconKind.WaterPercentLine, ForegroundBrush));
                    parts.Add(MakeText(info.Current.Humidity.ToString(), BodyFontSize, ForegroundBrush));
                    break;
                case 2:
                    parts.Add(Icon(PackIconRemixIconKind.WindyLine, ForegroundBrush));
                    parts.Add(MakeText(info.Current.Wind.Speed.ToString(), BodyFontSize, ForegroundBrush));
                    break;
                case 3:
                    parts.Add(Icon(PackIconRemixIconKind.LeafLine, AqiBrush(info.Aqi.AqiLevel)));
                    parts.Add(MakeText(info.Aqi.Aqi, BodyFontSize, ForegroundBrush));
                    break;
                case 4:
                    parts.Add(Icon(PackIconRemixIconKind.DashboardLine, ForegroundBrush));
                    parts.Add(MakeText(info.Current.Pressure.ToString(), BodyFontSize, ForegroundBrush));
                    break;
                case 5:
                    parts.Add(Icon(PackIconRemixIconKind.ThermometerLine, ForegroundBrush));
                    parts.Add(MakeText(info.Current.FeelsLike.ToString(), BodyFontSize, ForegroundBrush));
                    break;
            }
        }

        if (settings.ShowAlerts)
        {
            foreach (var alert in info.Alerts)
            {
                parts.Add(Icon(PackIconRemixIconKind.AlertLine, ForegroundBrush));
                if (settings.AlertsTitleShowMode != 2)
                    parts.Add(MakeText(alert.Type, SecondaryFontSize, ForegroundBrush));
            }
        }

        var rainMinutes = info.Minutely.Precipitation.RainRemainingMinutes;
        if (settings.ShowRainTime && rainMinutes != 0)
        {
            parts.Add(Icon(PackIconRemixIconKind.RainyLine, ForegroundBrush));
            parts.Add(MakeText($"{Math.Abs(rainMinutes)}min", SecondaryFontSize, ForegroundBrush));
        }

        return parts;
    }

    private FormattedText Icon(PackIconRemixIconKind kind, Brush brush)
        => MakeIcon(RemixIconGlyph.Get(kind), IconSize, brush);

    private FormattedText WeatherIcon(string code)
    {
        var name = Context.WeatherService.GetWeatherTextByCode(code) ?? "";
        var kind = name switch
        {
            _ when name.Contains('雷') => PackIconRemixIconKind.ThunderstormsLine,
            _ when name.Contains('雪') => PackIconRemixIconKind.SnowyLine,
            _ when name.Contains('雨') => PackIconRemixIconKind.RainyLine,
            _ when name.Contains('雾') || name.Contains('霾') => PackIconRemixIconKind.MistLine,
            _ when name.Contains('阴') => PackIconRemixIconKind.SunCloudyLine,
            _ when name.Contains('云') => PackIconRemixIconKind.CloudyLine,
            _ when name.Contains('晴') => PackIconRemixIconKind.SunLine,
            _ => PackIconRemixIconKind.CloudyLine
        };
        return Icon(kind, ForegroundBrush);
    }

    private Brush AqiBrush(int level)
    {
        var key = $"AqiLevel{Math.Min(Math.Max(level, 1), 6)}Brush";
        return Application.Current?.TryFindResource(key) as Brush ?? ForegroundBrush;
    }
}
