using ClassIsland.Core.Controls;

namespace ClassIsland.Core.Attributes;

/// <summary>
/// 提醒提供方信息
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class NotificationProviderInfo : Attribute
{
    /// <summary>
    /// 提醒提供方 GUID
    /// </summary>
    public Guid Guid { get; }

    /// <summary>
    /// 提醒提供方名称
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// 提醒提供方图标
    /// </summary>
    public string PackIcon { get; } = IconGlyphs.BellRing;

    /// <summary>
    /// 提醒提供方位图图标uri
    /// </summary>
    public string BitmapIconUri { get; } = "";

    /// <summary>
    /// 是否使用位图图标
    /// </summary>
    public bool UseBitmapIcon { get; } = false;

    /// <summary>
    /// 提醒提供方描述
    /// </summary>
    public string Description { get; } = "";

    /// <summary>
    /// 提醒提供方设置界面类型
    /// </summary>
    public Type? SettingsType { get; internal set; }

    /// <summary>
    /// 提醒提供方类型
    /// </summary>
    public Type? ProviderType { get; internal set; }

    /// <summary>
    /// 是否注册了设置类型
    /// </summary>
    public bool HasSettings { get; internal set; }

    /// <summary>
    /// 已注册的提醒渠道
    /// </summary>
    public List<NotificationChannelInfo> RegisteredChannels { get; } = [];


    /// <summary>
    /// 使用字形或位图图标初始化提醒提供方。单个 Segoe 字形视为图标，较长的路径视为位图。
    /// </summary>
    public NotificationProviderInfo(string guid, string name, string iconOrBitmapUri, string description) : this(guid, name,
        description)
    {
        if (IsGlyph(iconOrBitmapUri))
            PackIcon = iconOrBitmapUri;
        else
        {
            BitmapIconUri = iconOrBitmapUri;
            UseBitmapIcon = true;
        }
    }

    static bool IsGlyph(string value) =>
        value.Length is > 0 and <= 2 && value.IndexOfAny(['/', '\\', '.']) < 0;

    /// <inheritdoc />
    public NotificationProviderInfo(string guid, string name, string description = "")
    {
        Guid = Guid.Parse(guid);
        Name = name;
        Description = description;
    }
}