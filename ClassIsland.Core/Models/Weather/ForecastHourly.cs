using System.Text.Json.Serialization;

namespace ClassIsland.Core.Models.Weather;

public class ForecastHourly
{
    [JsonPropertyName("desc")] public string Description { get; set; } = "";
    [JsonPropertyName("aqi")] public StatusValueBase<List<int>> Aqi { get; set; } = new();
    [JsonPropertyName("temperature")] public StatusValueBase<List<int>> Temperature { get; set; } = new();
    [JsonPropertyName("weather")] public StatusValueBase<List<int>> Weather { get; set; } = new();
}