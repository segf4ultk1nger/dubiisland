using System.Text.Json.Serialization;

namespace ClassIsland.Core.Models.Weather;

/// <summary>
/// 生活指数集合（如紫外线、洗车、运动等）。
/// </summary>
public class WeatherIndices
{
    [JsonPropertyName("indices")] public List<WeatherIndexItem> Items { get; set; } = new();
}

public class WeatherIndexItem
{
    /// <summary>
    /// 指数类型，如 uvIndex、carWash、sports。
    /// </summary>
    [JsonPropertyName("type")] public string Type { get; set; } = "";

    /// <summary>
    /// 指数原始值。
    /// </summary>
    [JsonPropertyName("value")] public string Value { get; set; } = "";
}
