namespace ClassIsland.Core.Models.Weather;

public class City
{
    public string Name { get; set; } = "";

    /// <summary>
    /// 次级地区（省或上级市），用于搜索结果的静默副文本。
    /// </summary>
    public string Region { get; set; } = "";

    public string CityId { get; set; } = "";
}