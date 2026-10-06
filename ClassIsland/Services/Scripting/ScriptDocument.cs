using System;
using System.Collections.Generic;
using ClassIsland.Shared.Enums;
using Jint;
using TimeCrontab;

namespace ClassIsland.Services.Scripting;

/// <summary>
/// 表示一个已加载并处于运行状态的脚本文档。
/// </summary>
public class ScriptDocument
{
    /// <summary>
    /// 脚本文件路径。
    /// </summary>
    public string FilePath { get; }

    /// <summary>
    /// 脚本对应的 Jint 引擎实例。
    /// </summary>
    public Engine Engine { get; }

    /// <summary>
    /// 已注册的 cron 触发器。
    /// </summary>
    public List<ScriptCronEntry> CronEntries { get; } = new();

    /// <summary>
    /// 已注册的“特定时间点前”触发器。
    /// </summary>
    public List<ScriptPreTimePointEntry> PreTimePointEntries { get; } = new();

    /// <summary>
    /// 初始化一个 <see cref="ScriptDocument"/> 实例。
    /// </summary>
    /// <param name="filePath">脚本文件路径。</param>
    /// <param name="engine">脚本对应的 Jint 引擎实例。</param>
    public ScriptDocument(string filePath, Engine engine)
    {
        FilePath = filePath;
        Engine = engine;
    }
}

/// <summary>
/// 一个已注册的 cron 触发器。
/// </summary>
public sealed class ScriptCronEntry
{
    /// <summary>cron 表达式。</summary>
    public string Expression { get; init; } = "";

    /// <summary>解析后的 crontab。</summary>
    public Crontab Crontab { get; init; } = null!;

    /// <summary>对应的处理程序 ID。</summary>
    public int HandlerId { get; init; }

    /// <summary>下一次触发时间。</summary>
    public DateTime Next { get; set; }
}

/// <summary>
/// 一个已注册的“特定时间点前”触发器。
/// </summary>
public sealed class ScriptPreTimePointEntry
{
    /// <summary>目标时间状态。</summary>
    public TimeState State { get; init; } = TimeState.OnClass;

    /// <summary>提前秒数。</summary>
    public double Seconds { get; init; }

    /// <summary>对应的处理程序 ID。</summary>
    public int HandlerId { get; init; }

    /// <summary>上一次检查时间。</summary>
    public DateTime LastCheck { get; set; }
}
