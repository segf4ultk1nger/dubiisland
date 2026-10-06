namespace ClassIsland.Helpers;

/// <summary>设置搜索索引里的一条记录（一个设置卡片，或一个设置页面本身）。</summary>
public sealed class SettingsSearchEntry
{
    public string PageId { get; init; } = "";
    public string PageName { get; init; } = "";
    public string Header { get; init; } = "";
    public string Description { get; init; } = "";
    public string Tags { get; init; } = "";
    public bool IsPage { get; init; }
    public int Occurrence { get; init; }
    public string Pinyin { get; init; } = "";
    public string PinyinSpaced { get; init; } = "";
    public string Initials { get; init; } = "";
}
