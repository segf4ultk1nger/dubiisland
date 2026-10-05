using MahApps.Metro.IconPacks;

namespace ClassIsland.Models.Weather;

/// <summary>
/// 逐日预报的展示项。
/// </summary>
public class DailyForecastItem
{
    public string DayText { get; set; } = "";
    public string DateText { get; set; } = "";
    public PackIconRemixIconKind Icon { get; set; }
    public string High { get; set; } = "";
    public string Low { get; set; } = "";
    public string ConditionText { get; set; } = "";
    public bool HasPrecip { get; set; }
    public string PrecipProbability { get; set; } = "";
}

/// <summary>
/// 逐小时预报的展示项。
/// </summary>
public class HourlyForecastItem
{
    public string TimeText { get; set; } = "";
    public PackIconRemixIconKind Icon { get; set; }
    public string Temperature { get; set; } = "";
}
