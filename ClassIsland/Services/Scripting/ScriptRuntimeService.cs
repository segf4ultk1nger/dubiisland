using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using ClassIsland.Core.Abstractions.Services;
using Jint;
using Jint.Runtime;
using Microsoft.Extensions.Logging;

namespace ClassIsland.Services.Scripting;

/// <summary>
/// 脚本运行时服务。负责加载脚本，并在单一后台调度线程上执行所有脚本逻辑。
/// 由于 Jint 引擎非线程安全，所有引擎访问（顶层执行与事件处理）都必须在该线程上进行。
/// </summary>
public class ScriptRuntimeService : IDisposable
{
    /// <summary>
    /// 在用户脚本之前执行的预置脚本，用于在 JavaScript 侧维护事件处理程序注册表。
    /// </summary>
    private const string ScriptPrelude = @"
var __handlers = { startup: [], stopping: [], classStart: [], breakingTime: [], afterSchool: [] };
var on = {
  startup: function(h){ __handlers.startup.push(h); },
  stopping: function(h){ __handlers.stopping.push(h); },
  classStart: function(h){ __handlers.classStart.push(h); },
  breakingTime: function(h){ __handlers.breakingTime.push(h); },
  afterSchool: function(h){ __handlers.afterSchool.push(h); }
};
function __fire(name, ctx) {
  var list = __handlers[name] || [];
  for (var i = 0; i < list.length; i++) {
    var h = list[i];
    try {
      if (typeof h === 'function') h(ctx);
      else if (h && typeof h.do === 'function') h.do(ctx);
    } catch (e) { __logError('' + (e && e.stack ? e.stack : e)); }
  }
}
";

    /// <summary>
    /// 在脚本目录为空时创建的示例脚本。
    /// </summary>
    private const string SampleScript = @"on.startup(function(){ log(""LegacyIsland script runtime started""); });
on.classStart(function(){ log(""class started""); });
";

    private readonly ILogger<ScriptRuntimeService> _logger;
    private readonly INotificationHostService _notificationHostService;
    private readonly BlockingCollection<Action> _queue = new();
    private readonly List<ScriptDocument> _documents = new();
    private readonly object _syncRoot = new();
    private Thread? _thread;
    private bool _initialized;
    private bool _disposed;

    private delegate void LogDelegate(params object[] args);

    /// <summary>
    /// 初始化一个 <see cref="ScriptRuntimeService"/> 实例，并订阅课程事件。
    /// </summary>
    /// <param name="logger">日志记录器。</param>
    /// <param name="lessonsService">课程服务。</param>
    /// <param name="notificationHostService">提醒主机服务。</param>
    public ScriptRuntimeService(ILogger<ScriptRuntimeService> logger, ILessonsService lessonsService,
        INotificationHostService notificationHostService)
    {
        _logger = logger;
        _notificationHostService = notificationHostService;

        lessonsService.OnClass += (_, _) => FireEvent("classStart");
        lessonsService.OnBreakingTime += (_, _) => FireEvent("breakingTime");
        lessonsService.OnAfterSchool += (_, _) => FireEvent("afterSchool");
    }

    /// <summary>
    /// 启动脚本运行时，加载所有脚本并触发 <c>on.startup</c>。
    /// </summary>
    public void Initialize()
    {
        lock (_syncRoot)
        {
            if (_initialized || _disposed)
            {
                return;
            }

            _initialized = true;
        }

        _thread = new Thread(ProcessQueue)
        {
            IsBackground = true,
            Name = "LegacyIsland.ScriptRuntime"
        };
        _thread.Start();

        Post(Bootstrap);
    }

    /// <summary>
    /// 停止脚本运行时，触发 <c>on.stopping</c> 并停止调度线程。
    /// </summary>
    public void Shutdown()
    {
        lock (_syncRoot)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        if (!_initialized)
        {
            return;
        }

        using (var completed = new ManualResetEventSlim(false))
        {
            Post(() =>
            {
                FireEventCore("stopping");
                completed.Set();
            });
            completed.Wait(TimeSpan.FromSeconds(3));
        }

        _queue.CompleteAdding();
        try
        {
            _thread?.Join(TimeSpan.FromSeconds(3));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "停止脚本运行时线程失败。");
        }
    }

    private void ProcessQueue()
    {
        foreach (var action in _queue.GetConsumingEnumerable())
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "执行脚本调度任务时发生异常。");
            }
        }
    }

    private void Post(Action action)
    {
        if (_queue.IsAddingCompleted)
        {
            return;
        }

        try
        {
            _queue.Add(action);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void FireEvent(string name)
    {
        Post(() => FireEventCore(name));
    }

    private void FireEventCore(string name)
    {
        foreach (var document in _documents.ToArray())
        {
            try
            {
                document.Engine.Invoke("__fire", new object[] { name });
            }
            catch (JavaScriptException ex)
            {
                _logger.LogError(ex, "脚本“{File}”处理事件“{Event}”时发生异常：{Message}", document.FilePath, name,
                    ex.GetJavaScriptErrorString());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "脚本“{File}”处理事件“{Event}”时发生异常。", document.FilePath, name);
            }
        }
    }

    private void Bootstrap()
    {
        try
        {
            var directory = Path.Combine(App.AppConfigPath, "Scripts", "Default");
            Directory.CreateDirectory(directory);

            var files = Directory.GetFiles(directory, "*.js")
                .OrderBy(x => Path.GetFileName(x), StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (files.Length == 0)
            {
                var samplePath = Path.Combine(directory, "sample.js");
                File.WriteAllText(samplePath, SampleScript);
                files = new[] { samplePath };
            }

            foreach (var file in files)
            {
                LoadScript(file);
            }

            FireEventCore("startup");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "初始化脚本运行时失败。");
        }
    }

    private void LoadScript(string file)
    {
        try
        {
            var engine = CreateEngine();
            var bridge = new ScriptApiBridge(_logger, _notificationHostService);
            engine.SetValue("log", new LogDelegate(bridge.Log));
            engine.SetValue("run", new Action<string, string?>(bridge.Run));
            engine.SetValue("notify", new Action<string>(bridge.Notify));
            engine.SetValue("__logError", new Action<string>(bridge.LogError));

            engine.Execute(ScriptPrelude, "legacyisland:prelude");
            engine.Execute(File.ReadAllText(file), file);

            _documents.Add(new ScriptDocument(file, engine));
            _logger.LogInformation("已加载脚本：{}", file);
        }
        catch (JavaScriptException ex)
        {
            _logger.LogError(ex, "脚本“{File}”执行失败（{Location}）：{Message}", file, ex.Location,
                ex.GetJavaScriptErrorString());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "脚本“{File}”加载失败。", file);
        }
    }

    private static Engine CreateEngine()
    {
        var options = new Options();
        options.Strict(true);
        options.MaxStatements(1_000_000);
        options.TimeoutInterval(TimeSpan.FromSeconds(5));
        options.LimitMemory(64 * 1024 * 1024);
        options.Constraints.StackOverflowGuard = true;
        return new Engine(options);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Shutdown();
        GC.SuppressFinalize(this);
    }
}
