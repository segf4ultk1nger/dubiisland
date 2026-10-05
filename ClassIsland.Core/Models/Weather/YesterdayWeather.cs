using System.Text.Json.Serialization;

namespace ClassIsland.Core.Models.Weather;

/// <summary>
/// 昨日天气概况。
/// </summary>
public class YesterdayWeather
{
    [JsonPropertyName("aqi")] public string Aqi { get; set; } = "";
    [JsonPropertyName("date")] public DateTime Date { get; set; }
    [JsonPropertyName("sunRise")] public DateTime SunRise { get; set; }
    [JsonPropertyName("sunSet")] public DateTime SunSet { get; set; }
    [JsonPropertyName("tempMax")] public string TempMax { get; set; } = "";
    [JsonPropertyName("tempMin")] public string TempMin { get; set; } = "";
    [JsonPropertyName("weatherStart")] public string WeatherStart { get; set; } = "";
    [JsonPropertyName("weatherEnd")] public string WeatherEnd { get; set; } = "";
    [JsonPropertyName("windDircStart")] public string WindDirectionStart { get; set; } = "";
    [JsonPropertyName("windDircEnd")] public string WindDirectionEnd { get; set; } = "";
    [JsonPropertyName("windSpeedStart")] public string WindSpeedStart { get; set; } = "";
    [JsonPropertyName("windSpeedEnd")] public string WindSpeedEnd { get; set; } = "";
}
