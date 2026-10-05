using System.Text.Json.Serialization;

namespace ClassIsland.Core.Models.Weather;

public class StatusValueBase<T>
{
    [JsonPropertyName("value")] public T Value { get; set; } = default!;

    /// <summary>
    /// 该组数据的发布时间（部分字段不提供，如逐日数组）。
    /// </summary>
    [JsonPropertyName("pubTime")] public DateTime? PubTime { get; set; }
}