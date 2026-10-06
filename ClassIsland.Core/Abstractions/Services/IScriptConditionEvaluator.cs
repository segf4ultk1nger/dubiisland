using System;

namespace ClassIsland.Core.Abstractions.Services;

/// <summary>
/// 脚本条件求值器。把条件表达式字符串求值为布尔结果，并缓存求值状态。
/// </summary>
public interface IScriptConditionEvaluator
{
    /// <summary>
    /// 求值一个条件表达式。空表达式视为不满足。
    /// </summary>
    /// <param name="expression">条件表达式。</param>
    /// <returns>表达式是否满足。</returns>
    bool Evaluate(string? expression);

    /// <summary>
    /// 条件求值结果刷新时触发。
    /// </summary>
    event EventHandler? ConditionUpdated;
}
