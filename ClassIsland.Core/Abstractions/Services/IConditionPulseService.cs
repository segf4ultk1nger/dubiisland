namespace ClassIsland.Core.Abstractions.Services;

/// <summary>
/// 条件状态脉冲服务。在可能影响脚本条件求值的宿主状态发生变化时发出通知。
/// </summary>
public interface IConditionPulseService
{
    /// <summary>
    /// 当需要重新判定条件时触发。
    /// </summary>
    event EventHandler? StatusUpdated;

    /// <summary>
    /// 通知状态已变化，需要重新判定条件。
    /// </summary>
    void NotifyStatusChanged();
}
