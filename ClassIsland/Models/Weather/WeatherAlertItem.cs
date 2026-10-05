using System.Windows.Media;
using MahApps.Metro.IconPacks;

namespace ClassIsland.Models.Weather;

/// <summary>
/// 气象预警的展示项（等级配色 + 类型图标 + 展示文本）。
/// </summary>
public class WeatherAlertItem
{
    public string Title { get; set; } = "";
    public string Type { get; set; } = "";
    public string LevelText { get; set; } = "";
    public string Detail { get; set; } = "";
    public string TimeText { get; set; } = "";
    public bool IsExcluded { get; set; }

    public PackIconRemixIconKind Icon { get; set; }
    public Brush LevelBrush { get; set; } = Brushes.Gray;
    public Brush LevelTint { get; set; } = Brushes.Transparent;
}
