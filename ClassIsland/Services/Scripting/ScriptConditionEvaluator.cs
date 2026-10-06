using System;
using System.Collections.Concurrent;
using System.Threading;
using Acornima.Ast;
using ClassIsland.Core.Abstractions.Services;
using Jint;
using Jint.Native;
using Microsoft.Extensions.Logging;

namespace ClassIsland.Services.Scripting;

/// <summary>
/// 脚本条件求值器。持有独立于脚本运行时的 Jint 引擎，把条件表达式缓存为已编译脚本，
/// 并在状态脉冲到达时统一刷新。热路径 <see cref="Evaluate"/> 只读缓存，不在 Jint 上执行。
/// </summary>
public class ScriptConditionEvaluator : IScriptConditionEvaluator, IDisposable
{
    private sealed class ConditionEntry
    {
        internal Prepared<Script> Prepared;
        internal bool Compiled;
        internal volatile bool Result;
    }

    private readonly ILogger<ScriptConditionEvaluator> _logger;
    private readonly Engine _engine;
    private readonly IRulesetService _rulesetService;
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentDictionary<string, ConditionEntry> _cache = new();
    private readonly object _syncRoot = new();
    private bool _disposed;

    /// <inheritdoc />
    public event EventHandler? ConditionUpdated;

    /// <summary>
    /// 初始化一个 <see cref="ScriptConditionEvaluator"/> 实例。
    /// </summary>
    public ScriptConditionEvaluator(ILogger<ScriptConditionEvaluator> logger, ILessonsService lessonsService,
        IExactTimeService exactTimeService, IProfileService profileService, IWindowRuleService windowRuleService,
        IWeatherService weatherService, SettingsService settingsService, IRulesetService rulesetService)
    {
        _logger = logger;
        _rulesetService = rulesetService;

        _engine = CreateEngine();
        _engine.SetValue("lessons", new LessonsFacade(lessonsService, exactTimeService, profileService));
        _engine.SetValue("window", new WindowFacade(windowRuleService));
        _engine.SetValue("weather", new WeatherFacade(weatherService, settingsService));
        _engine.SetValue("time", new TimeFacade(exactTimeService));

        _rulesetService.StatusUpdated += OnStatusUpdated;
    }

    /// <inheritdoc />
    public bool Evaluate(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return false;
        }

        if (_cache.TryGetValue(expression!, out var entry))
        {
            return entry.Result;
        }

        return CompileAndCache(expression!);
    }

    /// <inheritdoc />
    public bool EvaluatePreview(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return false;
        }

        var prepared = Engine.PrepareScript(expression!, "preview:" + expression);
        lock (_syncRoot)
        {
            JsValue value = _engine.Evaluate(prepared);
            return value.AsBoolean();
        }
    }

    /// <summary>
    /// 使用当前门面状态重新计算所有缓存的条件，并发布刷新事件。
    /// </summary>
    public void Refresh()
    {
        lock (_syncRoot)
        {
            foreach (var pair in _cache)
            {
                EvaluateEntry(pair.Key, pair.Value);
            }
        }

        ConditionUpdated?.Invoke(this, EventArgs.Empty);
    }

    private bool CompileAndCache(string expression)
    {
        lock (_syncRoot)
        {
            if (_cache.TryGetValue(expression, out var cached))
            {
                return cached.Result;
            }

            var entry = new ConditionEntry();
            try
            {
                entry.Prepared = Engine.PrepareScript(expression, "condition:" + expression);
                entry.Compiled = true;
            }
            catch (Exception ex)
            {
                entry.Compiled = false;
                entry.Result = false;
                _logger.LogError(ex, "编译脚本条件“{Expression}”失败。", expression);
            }

            EvaluateEntry(expression, entry);
            _cache[expression] = entry;
            return entry.Result;
        }
    }

    private void EvaluateEntry(string expression, ConditionEntry entry)
    {
        if (!entry.Compiled)
        {
            entry.Result = false;
            return;
        }

        try
        {
            JsValue value = _engine.Evaluate(entry.Prepared);
            entry.Result = value.AsBoolean();
        }
        catch (Exception ex)
        {
            entry.Result = false;
            _logger.LogError(ex, "执行脚本条件“{Expression}”失败。", expression);
        }
    }

    private void OnStatusUpdated(object? sender, EventArgs e) => Refresh();

    private Engine CreateEngine()
    {
        var options = new Options();
        options.Strict(true);
        options.MaxStatements(1_000_000);
        options.TimeoutInterval(TimeSpan.FromSeconds(5));
        options.LimitMemory(64 * 1024 * 1024);
        options.Constraints.StackOverflowGuard = true;
        options.CancellationToken(_cts.Token);
        return new Engine(options);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _rulesetService.StatusUpdated -= OnStatusUpdated;
        _cts.Cancel();
        _engine.Dispose();
        _cts.Dispose();
        GC.SuppressFinalize(this);
    }
}
