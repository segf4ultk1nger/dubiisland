using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ClassIsland.Services.Scripting;

/// <summary>
/// 脚本清单，定义脚本目录中各脚本的元数据。
/// </summary>
public sealed class ScriptManifest
{
    /// <summary>清单版本。</summary>
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    /// <summary>配置方案名称。</summary>
    [JsonPropertyName("profile")]
    public string Profile { get; set; } = "Default";

    /// <summary>脚本条目。</summary>
    [JsonPropertyName("scripts")]
    public List<ScriptManifestEntry> Scripts { get; set; } = new();
}

/// <summary>
/// 脚本清单中的一个脚本条目。
/// </summary>
public sealed class ScriptManifestEntry
{
    /// <summary>脚本文件名。</summary>
    [JsonPropertyName("file")]
    public string File { get; set; } = "";

    /// <summary>显示名称。</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>是否启用。</summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>排序顺序。</summary>
    [JsonPropertyName("order")]
    public int Order { get; set; }
}

/// <summary>
/// 一条脚本日志，供脚本设置页面显示。
/// </summary>
public sealed class ScriptLogEntry
{
    /// <summary>来源脚本文件名。</summary>
    public string File { get; init; } = "";

    /// <summary>日志级别（info/warn/error）。</summary>
    public string Level { get; init; } = "info";

    /// <summary>日志内容。</summary>
    public string Message { get; init; } = "";
}
