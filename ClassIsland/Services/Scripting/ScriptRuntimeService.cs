using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Services.Automation.Triggers;
using ClassIsland.Shared.Enums;
using ClassIsland.Shared.Models.Profile;
using Jint;
using Jint.Native;
using Jint.Native.Object;
using Jint.Runtime;
using Jint.Runtime.Descriptors;
using Microsoft.Extensions.Logging;

namespace ClassIsland.Services.Scripting;

/// <summary>
/// 脚本运行时服务。负责加载脚本，并在单一后台调度线程上执行所有脚本逻辑。
/// 由于 Jint 引擎非线程安全，所有引擎访问（顶层执行与事件处理）都必须在该线程上进行。
/// </summary>
public class ScriptRuntimeService : IDisposable
{
    /// <summary>
    /// 在用户脚本之前执行的预置脚本，用于在 JavaScript 侧维护事件处理程序注册表与状态机。
    /// </summary>
    private const string ScriptPrelude = @"
var __handlers = [];
var __byId = {};
var __nextId = 1;
var __simple = {
  startup: [], stopping: [], classStart: [], breakingTime: [], afterSchool: [],
  timeStateChanged: [], conditionChanged: []
};
var __signal = [];
var __uri = [];

function __addRecord(spec) {
  var rec = { id: __nextId++, running: false, active: false, do: null, undo: null, when: null };
  if (typeof spec === 'function') { rec.do = spec; }
  else if (spec && typeof spec === 'object') {
    rec.do = typeof spec.do === 'function' ? spec.do : null;
    rec.undo = typeof spec.undo === 'function' ? spec.undo : null;
    rec.when = typeof spec.when === 'function' ? spec.when : null;
  }
  __handlers.push(rec);
  __byId[rec.id] = rec;
  return rec;
}
function __done(rec, payload) {
  if (!rec) return;
  if (rec.running) return;
  if (rec.undo && rec.active) return;
  if (rec.when) {
    try { if (!rec.when()) return; }
    catch (e) { __logError('' + (e && e.stack ? e.stack : e)); return; }
  }
  rec.running = true;
  if (rec.undo) rec.active = true;
  try { if (rec.do) rec.do(payload); }
  catch (e) { __logError('' + (e && e.stack ? e.stack : e)); }
  finally { rec.running = false; }
}
function __recover(rec) {
  if (!rec || !rec.active) return;
  rec.running = true;
  rec.active = false;
  try { if (rec.undo) rec.undo(); }
  catch (e) { __logError('' + (e && e.stack ? e.stack : e)); }
  finally { rec.running = false; }
}
function __fire(type, payload) {
  var list = __simple[type] || [];
  for (var i = 0; i < list.length; i++) __done(list[i], payload);
}
function __dispatch(id, payload) { __done(__byId[id], payload); }
function __recoverById(id) { __recover(__byId[id]); }
function __pulse() {
  for (var i = 0; i < __handlers.length; i++) {
    var r = __handlers[i];
    if (r.active && r.when) {
      try { if (!r.when()) __recover(r); }
      catch (e) { __logError('' + (e && e.stack ? e.stack : e)); }
    }
  }
}
function __shutdown() {
  __fire('stopping');
  for (var i = 0; i < __handlers.length; i++) { if (__handlers[i].active) __recover(__handlers[i]); }
}
function __signalFire(name, revert) {
  for (var i = 0; i < __signal.length; i++) {
    var e = __signal[i];
    if (e.name !== name) continue;
    if (revert) __recover(__byId[e.id]); else __done(__byId[e.id]);
  }
}
function __uriHandle(suffix, revert) {
  for (var i = 0; i < __uri.length; i++) {
    var e = __uri[i];
    if (e.suffix !== suffix) continue;
    if (revert) __recover(__byId[e.id]); else __done(__byId[e.id]);
  }
}
function __timeStateChanged(state, previous) { __fire('timeStateChanged', { state: state, previous: previous }); }
function __statusUpdated() { __fire('conditionChanged'); __pulse(); }

var on = {
  startup: function(h){ __simple.startup.push(__addRecord(h)); },
  stopping: function(h){ __simple.stopping.push(__addRecord(h)); },
  classStart: function(h){ __simple.classStart.push(__addRecord(h)); },
  breakingTime: function(h){ __simple.breakingTime.push(__addRecord(h)); },
  afterSchool: function(h){ __simple.afterSchool.push(__addRecord(h)); },
  timeStateChanged: function(h){ __simple.timeStateChanged.push(__addRecord(h)); },
  conditionChanged: function(h){ __simple.conditionChanged.push(__addRecord(h)); },
  signal: function(name,h){ __signal.push({ name: '' + name, id: __addRecord(h).id }); },
  uri: function(suffix,h){ __uri.push({ suffix: '' + suffix, id: __addRecord(h).id }); },
  cron: function(expr,h){ var r = __addRecord(h); __registerCron('' + expr, r.id); },
  preTimePoint: function(opts,h){
    opts = opts || {};
    var r = __addRecord(h);
    __registerPreTimePoint(opts.state ? '' + opts.state : 'OnClass', opts.seconds != null ? +opts.seconds : 60, r.id);
  }
};

var weather = {
  get isRefreshed(){ return __weather.isRefreshed; },
  get current(){ return __weather.current; },
  get currentText(){ return __weather.currentText; },
  get rainRemainingMinutes(){ return __weather.rainRemainingMinutes; },
  get alerts(){ return __weather.alerts; },
  rainIn: function(minutes, options){ options = options || {}; return __weather.rainIn(+minutes, !!options.remaining); },
  hasAlert: function(text, options){ options = options || {}; return __weather.hasAlert('' + text, !!options.regex); },
  isWeather: function(codeOrText){ return __weather.isWeather('' + codeOrText); }
};

function run(path, args){ __run('' + path, args == null ? null : '' + args); }
function notify(a){
  var o;
  if (typeof a === 'string' || typeof a === 'number' || typeof a === 'boolean') { o = { text: '' + a }; }
  else { o = a || {}; }
  var text = o.text != null ? '' + o.text : (o.content != null ? '' + o.content : '');
  var mask = o.mask != null ? '' + o.mask : text;
  var content = o.content != null ? '' + o.content : text;
  var duration = o.duration != null ? +o.duration : 0;
  var maskDuration = o.maskDuration != null ? +o.maskDuration : duration;
  __notify(mask, content, !!o.speech, !!o.topmost, !!o.sound, o.effect === undefined ? true : !!o.effect, duration, maskDuration);
}
function weatherNotify(kind){ __weatherNotify(kind | 0); }
function broadcast(name, revert){ __broadcast('' + name, !!revert); }
function setTheme(mode, primary, secondary){ __setTheme(mode | 0, primary == null ? null : '' + primary, secondary == null ? null : '' + secondary); }
function clearTheme(){ __clearTheme(); }
function setWindowDockingLocation(v){ __setWindowDockingLocation(v | 0); }
function clearWindowDockingLocation(){ __clearWindowDockingLocation(); }
function setWindowLayer(v){ __setWindowLayer(v | 0); }
function clearWindowLayer(){ __clearWindowLayer(); }
function setWindowDockingOffsetX(v){ __setWindowDockingOffsetX(v | 0); }
function clearWindowDockingOffsetX(){ __clearWindowDockingOffsetX(); }
function setWindowDockingOffsetY(v){ __setWindowDockingOffsetY(v | 0); }
function clearWindowDockingOffsetY(){ __clearWindowDockingOffsetY(); }
function switchComponentConfig(name){ __switchComponentConfig('' + name); }
function clearComponentConfig(){ __clearComponentConfig(); }
function navigate(uri){ __navigate('' + uri); }
function quit(){ __quit(); }
function restart(quiet){ __restart(!!quiet); }
function sleep(seconds){ return new Promise(function(resolve){ __sleep((seconds || 0) * 1000, resolve); }); }
";

    /// <summary>
    /// 在脚本目录为空时创建的示例脚本。
    /// </summary>
    private const string SampleScript = @"on.startup(function(){ log(""LegacyIsland script runtime started""); });
on.classStart(function(){ log(""class started""); });
";

    private readonly ILogger<ScriptRuntimeService> _logger;
    private readonly ILessonsService _lessonsService;
    private readonly IExactTimeService _exactTimeService;
    private readonly INotificationHostService _notificationHostService;
    private readonly SettingsService _settingsService;
    private readonly IUriNavigationService _uriNavigationService;
    private readonly IActionService _actionService;
    private readonly SignalTriggerHandlerService _signalTriggerHandlerService;
    private readonly ScriptApiBuilder _contributorApi = new();
    private readonly LessonsFacade _lessonsFacade;
    private readonly WindowFacade _windowFacade;
    private readonly WeatherFacade _weatherFacade;
    private readonly TimeFacade _timeFacade;
    private readonly AppFacade _appFacade = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly BlockingCollection<Action> _queue = new();
    private readonly List<ScriptDocument> _documents = new();
    private readonly object _syncRoot = new();
    private Timer? _cronTimer;
    private Thread? _thread;
    private bool _initialized;
    private bool _disposed;
    private TimeState _lastTimeState = TimeState.None;

    private delegate void LogDelegate(params object[] args);

    /// <summary>
    /// 脚本运行时取消令牌。
    /// </summary>
    internal CancellationToken CancellationToken => _cts.Token;

    /// <summary>
    /// 精确时间服务。
    /// </summary>
    internal IExactTimeService ExactTime => _exactTimeService;

    /// <summary>
    /// 初始化一个 <see cref="ScriptRuntimeService"/> 实例，并订阅宿主事件。
    /// </summary>
    public ScriptRuntimeService(ILogger<ScriptRuntimeService> logger, ILessonsService lessonsService,
        IExactTimeService exactTimeService, IRulesetService rulesetService, IWeatherService weatherService,
        SettingsService settingsService, IProfileService profileService, IWindowRuleService windowRuleService,
        INotificationHostService notificationHostService, SignalTriggerHandlerService signalTriggerHandlerService,
        UriTriggerHandlerService uriTriggerHandlerService, IActionService actionService,
        IUriNavigationService uriNavigationService, IEnumerable<IScriptApiContributor> contributors)
    {
        _logger = logger;
        _lessonsService = lessonsService;
        _exactTimeService = exactTimeService;
        _notificationHostService = notificationHostService;
        _settingsService = settingsService;
        _uriNavigationService = uriNavigationService;
        _actionService = actionService;
        _signalTriggerHandlerService = signalTriggerHandlerService;

        _lessonsFacade = new LessonsFacade(lessonsService, exactTimeService, profileService);
        _windowFacade = new WindowFacade(windowRuleService);
        _weatherFacade = new WeatherFacade(weatherService, settingsService);
        _timeFacade = new TimeFacade(exactTimeService);

        foreach (var contributor in contributors)
        {
            try
            {
                contributor.Configure(_contributorApi);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "配置脚本 API 贡献者 {Contributor} 失败。", contributor.GetType().FullName);
            }
        }

        _lastTimeState = lessonsService.CurrentState;

        lessonsService.OnClass += (_, _) => Post(() => InvokeAll("__fire", "classStart"));
        lessonsService.OnBreakingTime += (_, _) => Post(() => InvokeAll("__fire", "breakingTime"));
        lessonsService.OnAfterSchool += (_, _) => Post(() => InvokeAll("__fire", "afterSchool"));
        lessonsService.PostMainTimerTicked += (_, _) => Post(TickPreTimePoints);
        lessonsService.CurrentTimeStateChanged += (_, _) =>
        {
            var current = lessonsService.CurrentState;
            var previous = _lastTimeState;
            _lastTimeState = current;
            Post(() => InvokeAll("__timeStateChanged", current.ToString(), previous.ToString()));
        };
        rulesetService.StatusUpdated += (_, _) => Post(() => InvokeAll("__statusUpdated"));
        signalTriggerHandlerService.Handled += (_, e) =>
            Post(() => InvokeAll("__signalFire", e.SignalName, e.Revert));
        uriTriggerHandlerService.HandledRun += (_, e) =>
            Post(() => InvokeAll("__uriHandle", e.Name, false));
        uriTriggerHandlerService.HandledRevert += (_, e) =>
            Post(() => InvokeAll("__uriHandle", e.Name, true));
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

        _cronTimer = new Timer(_ => Post(EvaluateCrons), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

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
                InvokeAll("__shutdown");
                completed.Set();
            });
            completed.Wait(TimeSpan.FromSeconds(3));
        }

        _cts.Cancel();
        _cronTimer?.Dispose();
        _queue.CompleteAdding();
        try
        {
            _thread?.Join(TimeSpan.FromSeconds(3));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "停止脚本运行时线程失败。");
        }

        foreach (var document in _documents.ToArray())
        {
            try
            {
                document.Engine.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "释放脚本引擎“{File}”失败。", document.FilePath);
            }
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

    /// <summary>
    /// 将一个任务投递到脚本调度线程上执行。
    /// </summary>
    /// <param name="action">要执行的任务。</param>
    internal void Post(Action action)
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

            InvokeAll("__fire", "startup");
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
            var document = new ScriptDocument(file, engine);
            ConfigureEngine(engine, document);

            engine.Execute(ScriptPrelude, "legacyisland:prelude");
            ApplyBuilder(engine, _contributorApi);
            engine.Execute(File.ReadAllText(file), file);

            _documents.Add(document);
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

    private void ConfigureEngine(Engine engine, ScriptDocument document)
    {
        var bridge = new ScriptApiBridge(_logger, this, document, _notificationHostService, _settingsService,
            _uriNavigationService, _actionService, _signalTriggerHandlerService);

        engine.SetValue("log", new LogDelegate(bridge.Log));
        engine.SetValue("__logError", new Action<string>(bridge.LogError));
        engine.SetValue("__run", new Action<string, string?>(bridge.Run));
        engine.SetValue("__notify", new Action<string, string, bool, bool, bool, bool, double, double>(bridge.Notify));
        engine.SetValue("__weatherNotify", new Action<int>(bridge.WeatherNotify));
        engine.SetValue("__broadcast", new Action<string, bool>(bridge.Broadcast));
        engine.SetValue("__setTheme", new Action<int, string?, string?>(bridge.SetTheme));
        engine.SetValue("__clearTheme", new Action(bridge.ClearTheme));
        engine.SetValue("__setWindowDockingLocation", new Action<int>(bridge.SetWindowDockingLocation));
        engine.SetValue("__clearWindowDockingLocation", new Action(bridge.ClearWindowDockingLocation));
        engine.SetValue("__setWindowLayer", new Action<int>(bridge.SetWindowLayer));
        engine.SetValue("__clearWindowLayer", new Action(bridge.ClearWindowLayer));
        engine.SetValue("__setWindowDockingOffsetX", new Action<int>(bridge.SetWindowDockingOffsetX));
        engine.SetValue("__clearWindowDockingOffsetX", new Action(bridge.ClearWindowDockingOffsetX));
        engine.SetValue("__setWindowDockingOffsetY", new Action<int>(bridge.SetWindowDockingOffsetY));
        engine.SetValue("__clearWindowDockingOffsetY", new Action(bridge.ClearWindowDockingOffsetY));
        engine.SetValue("__switchComponentConfig", new Action<string>(bridge.SwitchComponentConfig));
        engine.SetValue("__clearComponentConfig", new Action(bridge.ClearComponentConfig));
        engine.SetValue("__navigate", new Action<string>(bridge.Navigate));
        engine.SetValue("__quit", new Action(() => App.Current.Stop()));
        engine.SetValue("__restart", new Action<bool>(quiet => App.Current.Restart(quiet)));
        engine.SetValue("__sleep", new Action<double, JsValue>(bridge.Sleep));
        engine.SetValue("__registerCron", new Action<string, int>(bridge.RegisterCron));
        engine.SetValue("__registerPreTimePoint", new Action<string, double, int>(bridge.RegisterPreTimePoint));

        engine.SetValue("lessons", _lessonsFacade);
        engine.SetValue("window", _windowFacade);
        engine.SetValue("__weather", _weatherFacade);
        engine.SetValue("time", _timeFacade);
        engine.SetValue("app", _appFacade);
    }

    private static void ApplyBuilder(Engine engine, ScriptApiBuilder api)
    {
        foreach (var global in api.Globals)
        {
            engine.SetValue(global.Key, global.Value);
        }

        foreach (var function in api.Functions)
        {
            engine.SetValue(function.Key, function.Value);
        }

        foreach (var ns in api.Namespaces)
        {
            engine.SetValue(ns.Key, BuildNamespaceObject(engine, ns.Value));
        }
    }

    private static ObjectInstance BuildNamespaceObject(Engine engine, ScriptApiBuilder api)
    {
        var obj = new JsObject(engine);
        foreach (var global in api.Globals)
        {
            obj.FastSetProperty(global.Key, new PropertyDescriptor(JsValue.FromObject(engine, global.Value),
                writable: true, enumerable: true, configurable: true));
        }

        foreach (var function in api.Functions)
        {
            obj.FastSetProperty(function.Key, new PropertyDescriptor(JsValue.FromObject(engine, function.Value),
                writable: true, enumerable: true, configurable: true));
        }

        foreach (var ns in api.Namespaces)
        {
            obj.FastSetProperty(ns.Key, new PropertyDescriptor(BuildNamespaceObject(engine, ns.Value),
                writable: true, enumerable: true, configurable: true));
        }

        return obj;
    }

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

    private void InvokeAll(string function, params object?[] args)
    {
        foreach (var document in _documents.ToArray())
        {
            InvokeOnDocument(document, function, args);
        }
    }

    private void InvokeOnDocument(ScriptDocument document, string function, params object?[] args)
    {
        try
        {
            document.Engine.Invoke(function, args);
        }
        catch (JavaScriptException ex)
        {
            _logger.LogError(ex, "脚本“{File}”执行 {Function} 时发生异常（{Location}）：{Message}", document.FilePath,
                function, ex.Location, ex.GetJavaScriptErrorString());
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "脚本“{File}”执行 {Function} 时发生异常。", document.FilePath, function);
        }
    }

    private void TickPreTimePoints()
    {
        var now = _exactTimeService.GetCurrentLocalDateTime();
        foreach (var document in _documents.ToArray())
        {
            foreach (var entry in document.PreTimePointEntries)
            {
                try
                {
                    var target = entry.State switch
                    {
                        TimeState.OnClass => _lessonsService.NextClassTimeLayoutItem,
                        TimeState.Breaking => _lessonsService.NextBreakingTimeLayoutItem,
                        _ => TimeLayoutItem.Empty
                    };

                    if (_lessonsService.CurrentState == entry.State)
                    {
                        InvokeOnDocument(document, "__recoverById", entry.HandlerId);
                        continue;
                    }

                    if (target == TimeLayoutItem.Empty || entry.Seconds < 0)
                    {
                        continue;
                    }

                    var targetTime = target.StartTime - TimeSpan.FromSeconds(entry.Seconds);
                    var targetDateTime = now.Date + targetTime;
                    if (entry.LastCheck < targetDateTime && targetDateTime <= now)
                    {
                        InvokeOnDocument(document, "__dispatch", entry.HandlerId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "脚本“{File}”处理特定时间点触发器时发生异常。", document.FilePath);
                }
                finally
                {
                    entry.LastCheck = now;
                }
            }
        }
    }

    private void EvaluateCrons()
    {
        var now = _exactTimeService.GetCurrentLocalDateTime();
        foreach (var document in _documents.ToArray())
        {
            foreach (var entry in document.CronEntries)
            {
                try
                {
                    if (now < entry.Next)
                    {
                        continue;
                    }

                    entry.Next = now + entry.Crontab.GetSleepTimeSpan(now);
                    InvokeOnDocument(document, "__dispatch", entry.HandlerId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "脚本“{File}”处理 cron 触发器“{Expression}”时发生异常。", document.FilePath,
                        entry.Expression);
                }
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Shutdown();
        _cts.Dispose();
        GC.SuppressFinalize(this);
    }
}
