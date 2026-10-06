using System;
using System.Collections.Generic;

namespace ClassIsland.Core.Abstractions.Services;

/// <summary>
/// 脚本 API 构建器，用于向所有脚本引擎注册全局对象、函数和命名空间。
/// </summary>
public class ScriptApiBuilder
{
    private readonly Dictionary<string, object?> _globals = new();
    private readonly Dictionary<string, Delegate> _functions = new();
    private readonly Dictionary<string, ScriptApiBuilder> _namespaces = new();

    /// <summary>
    /// 已注册的全局对象。
    /// </summary>
    public IReadOnlyDictionary<string, object?> Globals => _globals;

    /// <summary>
    /// 已注册的全局函数。
    /// </summary>
    public IReadOnlyDictionary<string, Delegate> Functions => _functions;

    /// <summary>
    /// 已注册的命名空间。
    /// </summary>
    public IReadOnlyDictionary<string, ScriptApiBuilder> Namespaces => _namespaces;

    /// <summary>
    /// 注册一个全局对象。
    /// </summary>
    /// <param name="name">全局名称。</param>
    /// <param name="value">对象值。</param>
    /// <returns>当前构建器，以便链式调用。</returns>
    public ScriptApiBuilder Global(string name, object? value)
    {
        _globals[name] = value;
        return this;
    }

    /// <summary>
    /// 注册一个全局函数。
    /// </summary>
    /// <param name="name">函数名称。</param>
    /// <param name="function">函数委托。</param>
    /// <returns>当前构建器，以便链式调用。</returns>
    public ScriptApiBuilder Function(string name, Delegate function)
    {
        _functions[name] = function;
        return this;
    }

    /// <summary>
    /// 获取（或创建）一个命名空间构建器，其上的注册会挂载到该命名空间对象下。
    /// </summary>
    /// <param name="name">命名空间名称。</param>
    /// <returns>该命名空间对应的构建器。</returns>
    public ScriptApiBuilder Namespace(string name)
    {
        if (!_namespaces.TryGetValue(name, out var builder))
        {
            builder = new ScriptApiBuilder();
            _namespaces[name] = builder;
        }

        return builder;
    }
}
