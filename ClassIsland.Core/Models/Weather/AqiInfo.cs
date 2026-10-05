using System.Text.Json.Serialization;

namespace ClassIsland.Core.Models.Weather;

public class AqiInfo
{
    [JsonPropertyName("aqi")] public string Aqi { get; set; } = "0.0";

    [JsonPropertyName("pm25")] public string Pm25 { get; set; } = "";
    [JsonPropertyName("pm10")] public string Pm10 { get; set; } = "";
    [JsonPropertyName("no2")] public string No2 { get; set; } = "";
    [JsonPropertyName("so2")] public string So2 { get; set; } = "";
    [JsonPropertyName("co")] public string Co { get; set; } = "";
    [JsonPropertyName("o3")] public string O3 { get; set; } = "";

    /// <summary>
    /// 首要污染物代码（如 pm10）。无首要污染物时为空。
    /// </summary>
    [JsonPropertyName("primary")] public string Primary { get; set; } = "";

    /// <summary>
    /// 数据来源，如“中国环境监测总站”。
    /// </summary>
    [JsonPropertyName("src")] public string Source { get; set; } = "";

    /// <summary>
    /// 空气质量建议文案。
    /// </summary>
    [JsonPropertyName("suggest")] public string Suggest { get; set; } = "";

    [JsonIgnore] public int AqiLevel => GetAqi();

    [JsonIgnore]
    public string AqiLevelText => AqiLevel switch
    {
        1 => "优",
        2 => "良",
        3 => "轻度污染",
        4 => "中度污染",
        5 => "重度污染",
        6 => "严重污染",
        _ => "未知"
    };

    private int GetAqi()
    {
        var aqi = double.TryParse(Aqi, out var r) ? r : 0.0;
        switch (aqi)
        {
            case <= 50:
                return 1;
            case > 50 and <= 100:
                return 2;
            case > 100 and <= 150:
                return 3;
            case > 150 and <= 200:
                return 4;
            case > 200 and <= 300:
                return 5;
            case > 300:
                return 6;
            default:
                return -1;
        }
    }
}