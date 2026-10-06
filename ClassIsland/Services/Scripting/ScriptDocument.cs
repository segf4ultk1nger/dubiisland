using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ClassIsland.Shared.Enums;
using Jint;
using TimeCrontab;

namespace ClassIsland.Services.Scripting;

/// <summary>
/// 脚本状态。
/// </summary>
public enum ScriptStatus
{
    /// <summary>已加载并运行。</summary>
    Loaded,

    /// <summary>加载或执行失败。</summary>
    Faulted,

    /// <summary>已禁用。</summary>
    Disabled
}

/// <summary>
/// 表示一个已加载并处于运行状态的脚本文档。
/// </summary>
public class ScriptDocument : INotifyPropertyChanged
{
    /// <summary>
    /// 脚本文件路径。
    /// </summary>
    public string FilePath { get; }

    /// <summary>
    /// 脚本文件名。
    /// </summary>
    public string FileName => System.IO.Path.GetFileName(FilePath);

    private string _name = "";
    private bool _enabled = true;
    private int _order;
    private ScriptStatus _status = ScriptStatus.Disabled;
    private string? _lastError;

    /// <summary>
    /// 脚本显示名称。
    /// </summary>
    public string Name
    {
        get => _name;
        set => SetField(ref _name, value ?? "");
    }

    /// <summary>
    /// 是否启用。
    /// </summary>
    public bool Enabled
    {
        get => _enabled;
        set => SetField(ref _enabled, value);
    }

    /// <summary>
    /// 排序顺序。
    /// </summary>
    public int Order
    {
        get => _order;
        set => SetField(ref _order, value);
    }

    /// <summary>
    /// 脚本状态。
    /// </summary>
    public ScriptStatus Status
    {
        get => _status;
        set => SetField(ref _status, value);
    }

    /// <summary>
    /// 最后一次错误信息，无错误时为 null。
    /// </summary>
    public string? LastError
    {
        get => _lastError;
        set => SetField(ref _lastError, value);
    }

    /// <summary>
    /// 脚本对应的 Jint 引擎实例。脚本被禁用或加载失败时为 null。
    /// </summary>
    public Engine? Engine { get; set; }

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
    public ScriptDocument(string filePath, string name, bool enabled, int order)
    {
        FilePath = filePath;
        _name = name;
        _enabled = enabled;
        _order = order;
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
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
