namespace Org.Sifware.UiAccessX;

/// <summary>插件设置，持久化到插件配置目录的 settings.json。</summary>
public sealed class UiAccessXSettings
{
    /// <summary>是否启用 UIAccess 超级置顶。开启后启动时会尝试以 UIAccess 身份重启自身。</summary>
    public bool EnableUiAccess { get; set; }
}
