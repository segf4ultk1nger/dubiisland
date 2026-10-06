using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Speech.Synthesis;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ClassIsland.Controls.AttachedSettingsControls;
using ClassIsland.Controls.Components;
using ClassIsland.Controls.Island;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Abstractions.Services.Management;
using ClassIsland.Core.Commands;
using ClassIsland.Core.Controls;
using ClassIsland.Core.Controls.CommonDialog;
using ClassIsland.Core.Extensions.Registry;
using ClassIsland.Shared;
using ClassIsland.Shared.Abstraction.Services;
using ClassIsland.Models;
using ClassIsland.Services;
using ClassIsland.ViewModels;
using ClassIsland.Services.AppUpdating;
using ClassIsland.Services.Logging;
using ClassIsland.Services.Management;
using ClassIsland.Services.NotificationProviders;
using ClassIsland.Services.Scripting;
using ClassIsland.Services.SpeechService;
using ClassIsland.Views;
using ClassIsland.Views.SettingPages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using LogLevel = Microsoft.Extensions.Logging.LogLevel;
using UpdateStatus = ClassIsland.Shared.Enums.UpdateStatus;
#if DEBUG
using JetBrains.Profiler.Api;
#endif
using ClassIsland.Core;
using ClassIsland.Controls.AuthorizeProvider;
using ClassIsland.Core.Enums;
#if IsMsix
using Windows.ApplicationModel;
using Windows.Storage;
#endif
using ClassIsland.Core.Abstractions.Services.Metadata;
using ClassIsland.Core.Abstractions.Views;
using ClassIsland.Core.Helpers;
using ClassIsland.Core.Models.Logging;
using ClassIsland.Services.Metadata;
using ClassIsland.Shared.Helpers;
using Microsoft.Extensions.Logging.Console;
using Walterlv.Threading;
using Walterlv.Windows;
using ClassIsland.Controls.NotificationProviders;
using System.Text;
using ClassIsland.Controls.SpeechProviderSettingsControls;
using ClassIsland.Core.Abstractions.Services.SpeechService;
namespace ClassIsland;
/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : AppBase, IAppHost
{
    internal Dispatcher? ThreadedUiDispatcher { get; set; }

    public static bool IsAssetsTrimmedInternal { get; } =
#if TrimAssets 
        true;
#else
        false;
#endif
    
    private CrashWindow? CrashWindow;
    public Mutex? Mutex { get; set; }
    public bool IsMutexCreateNew { get; set; } = false;
    private ILogger<App>? Logger { get; set; }
    //public static IHost? Host;

    public static readonly string AppRootFolderPath =
#if IsMsix
        ApplicationData.Current.LocalFolder.Path;
#else
        "./";
#endif
    public static readonly string AppDataFolderPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClassIsland");

    public static readonly string AppLogFolderPath = Path.Combine(AppRootFolderPath, "Logs");

    public static readonly string AppConfigPath = Path.Combine(AppRootFolderPath, "Config");

    public static readonly string AppCacheFolderPath =
#if IsMsix
        ApplicationData.Current.LocalCacheFolder.Path;
#else
        Path.Combine(AppRootFolderPath, "Cache");
#endif

    public static readonly string AppTempFolderPath =
#if IsMsix
        ApplicationData.Current.TemporaryFolder.Path;
#else
        Path.Combine(AppRootFolderPath, "Temp");
#endif

    public static T GetService<T>() => IAppHost.GetService<T>();

    private bool _isStartedCompleted = false;

    private bool _startupCompleted;

    private bool _mainWindowInitialized;

    internal static bool IsCrashed { get; set; } = false;

    internal static bool _isCriticalSafeModeEnabled = false;

    internal static bool AutoDisableCorruptPlugins
    {
        get
        {
            try
            {
                return ((App)Current).Settings.AutoDisableCorruptPlugins;
            }
            catch
            {
                return true;
            }
        }
    }

    public override bool IsDevelopmentBuild =>
#if DevelopmentBuild
        true
#else
        false
#endif
    ;

    public override bool IsMsix => _isMsix;

    public static readonly bool _isMsix =
#if IsMsix
        true
#else
        false
#endif
    ;

    public override string OperatingSystem => "windows";
    public override string Platform =>
#if PLATFORM_x64
    "x64"
#elif PLATFORM_x86
    "x86"
#elif PLATFORM_ARM64
    "arm64"
#elif PLATFORM_ARM
    "arm"
#elif PLATFORM_Any
    "any"
#else
    #if PublishBuilding
    #error "在发布构建中不应出现未知架构"
    #endif
    "unknown"
#endif
        ;
    public App()
    {
        //AppContext.SetSwitch("Switch.System.Windows.Input.Stylus.EnablePointerSupport", true);
        //TaskScheduler.UnobservedTaskException += TaskSchedulerOnUnobservedTaskException;
    }

    private static void CurrentDomainOnProcessExit(object? sender, EventArgs e)
    {
        if (IsCrashed)
        {
            return;
        }

        try
        {
            var startupCountFilePath = Path.Combine(AppRootFolderPath, ".startup-count");
            if (File.Exists(startupCountFilePath))
            {
                File.Delete(startupCountFilePath);
            }
        }
        catch (Exception exception)
        {
            // ignored
        }
    }

    static App()
    {
        DependencyPropertyHelper.ForceOverwriteDependencyPropertyDefaultValue(ToolTipService.InitialShowDelayProperty,
            0);
    }

    private void TaskSchedulerOnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        e.SetObserved();
        Dispatcher.Invoke(() =>
        {
            ProcessUnhandledException(e.Exception);
        });
    }

    public static ApplicationCommand ApplicationCommand
    {
        get;
        set;
    } = new();

    public Settings Settings { get; set; } = new();

    private void App_OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // COMException: UCEERR_RENDERTHREADFAILURE (0x88980406)
        if (e.Exception is COMException comException && (uint)comException.HResult == 0x88980406)
        {
            ProcessWpfCriticalException(e.Exception);
            return;
        }
        try
        {
            ProcessUnhandledException(e.Exception);
        }
        catch
        {
            ProcessWpfCriticalException(e.Exception);
        }
        e.Handled = true;
    }

    private void ProcessWpfCriticalException(Exception ex)
    {
        Exit += (_, _) =>
        {
            DiagnosticService.ProcessCriticalException(ex);
        };
        Stop();
    }

    internal void ProcessUnhandledException(Exception e, bool critical=false)
    {
#if DEBUG
        if (e.GetType() == typeof(ResourceReferenceKeyNotFoundException))
        {
            return;
        }
#endif
        Logger?.LogCritical(e, "发生严重错误");
        IsCrashed = true;
        var safe = _isCriticalSafeModeEnabled && (!(IAppHost.TryGetService<IWindowRuleService>()?.IsForegroundWindowClassIsland() ?? false));

        //Settings.DiagnosticCrashCount++;
        //Settings.DiagnosticLastCrashTime = DateTime.Now;

        var plugins = DiagnosticService.GetPluginsByStacktrace(e);
        var disabled = DiagnosticService.DisableCorruptPlugins(plugins);
        if (!safe)
        {
            var crashInfo = e.ToString();
            if (plugins.Count > 0)
            {
                var pluginsWarning = "此问题可能由以下插件引起，请在向 LegacyIsland 开发者反馈问题前先向以下插件的开发者反馈此问题：\n"
                                     + string.Join("\n", plugins.Select(x => $"- {x.Manifest.Name} [{x.Manifest.Id}]"))
                                     + (disabled
                                         ? "\n以上异常插件已自动禁用，重启应用后生效。您可以在排除问题后前往【应用设置】->【插件】中重新启用这些插件，或在【应用设置】->【基本】中调整是否自动禁用异常插件。"
                                         : "")
                    + "\n================================\n";
                crashInfo = pluginsWarning + crashInfo;
            }
            CrashWindow = new CrashWindow()
            {
                CrashInfo = crashInfo,
                AllowIgnore = _isStartedCompleted && !critical,
                IsCritical = critical
            };
            CrashWindow.ShowDialog();
            return;
        }

        switch (Settings.CriticalSafeModeMethod)
        {
            case 2 when critical:
            case 3 when critical:
            case 0:
                Logger?.LogInformation("因教学安全模式设定，应用将自动退出");
                Stop();
                break;
            case 1:
                Logger?.LogInformation("因教学安全模式设定，应用将自动静默重启");
                Restart(["-q", "-m"]);
                break;
            case 2:
                Logger?.LogInformation("因教学安全模式设定，应用将忽略异常并显示一条通知");
                IAppHost.Host?.Services.GetService<ITaskBarIconService>()?.ShowNotification("崩溃报告", $"LegacyIsland 发生了一个无法处理的错误：{e.Message}");
                break;
            case 3:
                Logger?.LogInformation("因教学安全模式设定，应用将直接忽略异常");
                break;
            default:
                Logger?.LogWarning("无效的教学安全模式设置：{}", Settings.CriticalSafeModeMethod);
                break;
        }
    }

    private async void App_OnStartup(object sender, StartupEventArgs e)
    {
        AppBase.CurrentLifetime = ApplicationLifetime.Initializing;
        MyWindow.ShowOssWatermark = ApplicationCommand.ShowOssWatermark;
        // 提前套用默认主题：启动检查、单实例提示、闪屏等窗口创建于主题应用（ThemeApplyService）之前，
        // 若不先加载 MahApps 主题资源，这些窗口会渲染成全黑。
        ThemeService.ApplyStartupTheme();
        //DependencyPropertyHelper.ForceOverwriteDependencyPropertyDefaultValue(FrameworkElement.FocusVisualStyleProperty,
        //    Resources[SystemParameters.FocusVisualStyleKey]);
        Environment.CurrentDirectory = System.Windows.Forms.Application.StartupPath;

        //ConsoleService.InitializeConsole();
        System.Windows.Forms.Application.EnableVisualStyles();
        DiagnosticService.BeginStartup();
        ConsoleService.InitializeConsole();
        //if (IsAssetsTrimmed())
        //{
        //    Resources["HarmonyOsSans"] = FindResource("BackendFontFamily");
        //}

        PresentationTraceSources.DataBindingSource.Listeners.Add(new BindingFailureTraceListener(this));
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;

        Thread.CurrentThread.CurrentUICulture = new CultureInfo("zh-CN");
        Thread.CurrentThread.CurrentCulture = new CultureInfo("zh-CN");
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(System.Windows.Markup.XmlLanguage.GetLanguage(CultureInfo.CurrentUICulture.IetfLanguageTag)));

        // 检测Mutex
        if (!IsMutexCreateNew)
        {
            if (!ApplicationCommand.WaitMutex)
            {
                ProcessInstanceExisted();
                Environment.Exit(0);
            }
        }

        // 检测临时目录
        if (Environment.CurrentDirectory.Contains(Path.GetTempPath()))
        {
            CommonDialog.ShowHint("LegacyIsland正在临时目录下运行，应用设置、课表等数据很可能无法保存，或在应用退出后被自动删除。在使用本应用前，请务必将本应用解压到一个适合的位置。");
            Environment.Exit(0);
            return;
        }

        // 检测桌面文件夹
        if (Environment.CurrentDirectory == Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory))
        {
            var r = CommonDialog.ShowHint("LegacyIsland正在桌面上运行，应用设置、课表等数据将会直接存放到桌面上。在使用本应用前，请将本应用移动到一个单独的文件夹中。");
            if (r == 0)
            {
                Environment.Exit(0);
                return;
            }
        }

        // 检测目录是否可以访问
        try
        {
            var testWritePath = Path.Combine(AppRootFolderPath, "./.test-write");
            await FrameworkCompat.WriteAllTextAsync(testWritePath, "");
            File.Delete(testWritePath);
        }
        catch (Exception ex)
        {
            CommonDialog.ShowError($"LegacyIsland无法写入当前目录：{ex.Message}\n\n请将本软件解压到一个合适的位置后再运行。");
            Environment.Exit(0);
            return;
        }

        // 检测 DWM
        DwmIsCompositionEnabled(out var isDwmEnabled);
        if (!isDwmEnabled)
        {
            CommonDialog.ShowError("运行LegacyIsland需要开启Aero效果。请在【控制面板】->【个性化】中启用Aero主题，然后再尝试运行LegacyIsland。");
            Environment.Exit(0);
            return;
        }

        var startupCountFilePath = Path.Combine(AppRootFolderPath, ".startup-count");
        var startupCount = File.Exists(startupCountFilePath)
            ? (int.TryParse(await FrameworkCompat.ReadAllTextAsync(startupCountFilePath), out var count) ? count + 1 : 1)
            : 1;
        if (startupCount >= 5 && ApplicationCommand is { Recovery: false, Quiet: false })
        {
            var enterRecovery = new CommonDialogBuilder()
                .SetIconKind(CommonDialogIconKind.Hint)
                .SetContent("LegacyIsland 多次启动失败，您需要进入恢复模式以尝试修复 LegacyIsland 吗？")
                .AddCancelAction()
                .AddAction("进入恢复模式", IconGlyphs.WrenchCheckOutline, true)
                .ShowDialog();
            if (enterRecovery == 1)
            {
                ApplicationCommand.Recovery = true;
            }
        }
        if (ApplicationCommand.Recovery)
        {
            if (File.Exists(startupCountFilePath))
            {
                File.Delete(startupCountFilePath);
            }
            
            var recoveryWindow = new RecoveryWindow();
            recoveryWindow.Show();
            return;
        }

        
        await FrameworkCompat.WriteAllTextAsync(startupCountFilePath, startupCount.ToString());
        AppDomain.CurrentDomain.ProcessExit += CurrentDomainOnProcessExit;

        if (ApplicationCommand.UpdateReplaceTarget != null)
        {
            //MessageBox.Show($"Update replace {ApplicationCommand.UpdateReplaceTarget}");
            await UpdateService.ReplaceApplicationFile(ApplicationCommand.UpdateReplaceTarget);
            Process.Start(new ProcessStartInfo()
            {
                FileName = ApplicationCommand.UpdateReplaceTarget,
                Arguments = FrameworkCompat.JoinArguments("-udt", FrameworkCompat.ProcessPath, "-m", "true")
            });
            Stop();
            return;
        }
        if (ApplicationCommand.UpdateDeleteTarget != null)
        {            
            //MessageBox.Show($"Update DELETE {ApplicationCommand.UpdateDeleteTarget}");
            UpdateService.RemoveUpdateTemporary(ApplicationCommand.UpdateDeleteTarget);
        }

        FileFolderService.CreateFolders();
        PluginService.ProcessPluginsInstall();
        bool isSystemSpeechSystemExist = false;

        IAppHost.Host = Microsoft.Extensions.Hosting.Host.
            CreateDefaultBuilder().
            UseContentRoot(AppContext.BaseDirectory).
            ConfigureServices((context, services) =>
            {
                services.AddSingleton<SettingsService>();
                services.AddSingleton<UpdateService>();
                services.AddSingleton<ITaskBarIconService, TaskBarIconService>();
                services.AddSingleton<TrayIconService>();
                services.AddSingleton<WallpaperPickingService>();
                services.AddSingleton<INotificationHostService, NotificationHostService>();
                services.AddSingleton<NotificationDisplayService>();
                services.AddSingleton<IThemeService, ThemeService>();
                services.AddSingleton<ThemeApplyService>();
                services.AddSingleton<MiniInfoProviderHostService>();
                services.AddSingleton<IWeatherService, WeatherService>();
                services.AddSingleton<FileFolderService>();
                services.AddSingleton<IAttachedSettingsHostService, AttachedSettingsHostService>();
                services.AddSingleton<IProfileService, ProfileService>();
                services.AddSingleton<ISplashService, SplashService>();
                services.AddSingleton<IHangService, HangService>();
                services.AddSingleton<ConsoleService>();
                //services.AddHostedService<BootService>();
                services.AddSingleton<UpdateNodeSpeedTestingService>();
                services.AddSingleton<DiagnosticService>();
                services.AddSingleton<IManagementService, ManagementService>();
                services.AddSingleton<AppLogService>();
                services.AddSingleton<IComponentsService, ComponentsService>();
                services.AddSingleton<ILessonsService, LessonsService>();
                services.AddSingleton<IUriNavigationService, UriNavigationService>();
                services.AddHostedService<MemoryWatchDogService>();
                services.AddSingleton<IPluginService, PluginService>();
                services.AddSingleton<IPluginMarketService, PluginMarketService>();
                services.AddSingleton<IConditionPulseService, ConditionPulseService>();
                services.AddSingleton<IWindowRuleService, WindowRuleService>();
                services.AddSingleton<ScriptRuntimeService>();
                services.AddSingleton<ScriptConditionEvaluator>();
                services.AddSingleton<IScriptConditionEvaluator>(s => s.GetRequiredService<ScriptConditionEvaluator>());
                services.AddSingleton<LegacyImporter>();
                services.AddSingleton<ISpeechService>(GetSpeechService);
                services.AddSingleton<IExactTimeService, ExactTimeService>();
                //services.AddSingleton(typeof(ApplicationCommand), ApplicationCommand);
                services.AddSingleton<IProfileAnalyzeService, ProfileAnalyzeService>();
                services.AddSingleton<IAuthorizeService, AuthorizeService>();
                services.AddSingleton<UriTriggerHandlerService>();
                services.AddSingleton<SignalTriggerHandlerService>();
                services.AddSingleton<IAnnouncementService, AnnouncementService>();
                services.AddSingleton<ILocationService, LocationService>();
                // Views
                services.AddSingleton<MainViewModel>(s => new MainViewModel
                {
                    Settings = s.GetRequiredService<SettingsService>().Settings,
                    Profile = s.GetRequiredService<IProfileService>().Profile
                });
                services.AddSingleton<MainWindow>();
                services.AddSingleton<IslandHost>();
                services.AddTransient<SplashWindowBase, SplashWindow>();
                services.AddTransient<FeatureDebugWindow>();
                services.AddSingleton<TopmostEffectWindow>(BuildTopmostEffectWindow);
                services.AddSingleton<ToolWindowManager>();
                services.AddTransient<AppLogsWindow>(s => s.GetRequiredService<ToolWindowManager>().Get<AppLogsWindow>());
                services.AddTransient<SettingsWindowNew>(s => s.GetRequiredService<ToolWindowManager>().Get<SettingsWindowNew>());
                services.AddTransient<ProfileSettingsWindow>(s => s.GetRequiredService<ToolWindowManager>().Get<ProfileSettingsWindow>());
                services.AddTransient<ClassPlanDetailsWindow>();
                services.AddTransient<WindowRuleDebugWindow>();
                services.AddTransient<ConfigErrorsWindow>();
                services.AddTransient<TimeAdjustmentWindow>();
                // 设置页面
                services.AddSettingsPage<GeneralSettingsPage>();
                services.AddSettingsPage<ComponentsSettingsPage>();
                services.AddSettingsPage<AppearanceSettingsPage>();
                services.AddSettingsPage<NotificationSettingsPage>();
                services.AddSettingsPage<WindowSettingsPage>();
                services.AddSettingsPage<WeatherSettingsPage>();
                //services.AddSettingsPage<AutomationSettingsPage>();
                services.AddSettingsPage<ScriptsSettingsPage>();
                services.AddSettingsPage<StorageSettingsPage>();
                services.AddSettingsPage<PluginsSettingsPage>();
                services.AddSettingsPage<TestSettingsPage>();
                services.AddSettingsPage<DebugPage>();
                services.AddSettingsPage<DebugBrushesSettingsPage>();
                services.AddSettingsPage<AboutSettingsPage>();
                services.AddSettingsPage<ManagementSettingsPage>();
                services.AddSettingsPage<ManagementCredentialsSettingsPage>();
                services.AddSettingsPage<ManagementPolicySettingsPage>();
                // 主界面组件
                services.AddComponent<TextComponent, TextComponentSettingsControl>();
                services.AddComponent<SeparatorComponent>();
                services.AddComponent<ScheduleComponent, ScheduleComponentSettingsControl>();
                services.AddComponent<DateComponent>();
                services.AddComponent<ClockComponent, ClockComponentSettingsControl>();
                services.AddComponent<WeatherComponent, WeatherComponentSettingsControl>();
                services.AddComponent<CountDownComponent, CountDownComponentSettingsControl>();
                services.AddComponent<SlideComponent, SlideComponentSettingsControl>();
                services.AddComponent<RollingComponent, RollingComponentSettingsControl>();
                services.AddComponent<GroupComponent>();
                services.AddComponent<StackComponent>();
                // 提醒提供方
                services.AddNotificationProvider<ClassNotificationProvider, ClassNotificationProviderSettingsControl>();
                services.AddNotificationProvider<AfterSchoolNotificationProvider, AfterSchoolNotificationProviderSettingsControl>();
                services.AddNotificationProvider<WeatherNotificationProvider, WeatherNotificationProviderSettingsControl>();
                // Transients
                services.AddTransient<WallpaperPreviewWindow>();
                // Logging
                services.AddLogging(builder =>
                {
                    LogMaskingHelper.Rules.Add(new LogMaskRule(new(@"(latitude=)(\d*\.?\d*)"), 2));
                    LogMaskingHelper.Rules.Add(new LogMaskRule(new(@"(longitude=)(\d*\.?\d*)"), 2));

                    builder.AddConsoleFormatter<ClassIslandConsoleFormatter, ConsoleFormatterOptions>();
                    builder.AddConsole(console =>
                    {
                        console.FormatterName = "classisland";
                    });
                    var debug = false;
#if DEBUG
                    debug = true;
#endif
                    if (ApplicationCommand.Verbose || debug)
                    {
                        builder.SetMinimumLevel(LogLevel.Trace);
                    }
                });
                services.AddSingleton<ILoggerProvider, AppLoggerProvider>();
                services.AddSingleton<ILoggerProvider, FileLoggerProvider>();
                // AttachedSettings
                services.AddAttachedSettingsControl<AfterSchoolNotificationAttachedSettingsControl>();
                services.AddAttachedSettingsControl<ClassNotificationAttachedSettingsControl>();
                services.AddAttachedSettingsControl<LessonControlAttachedSettingsControl>();
                services.AddAttachedSettingsControl<WeatherNotificationAttachedSettingsControl>();
                // 认证提供方
                services.AddAuthorizeProvider<PasswordAuthorizeProvider>();
                // 语音提供方
                services.AddSpeechProvider<SystemSpeechService>();
                services.AddSpeechProvider<EdgeTtsService, EdgeTtsSpeechServiceSettingsControl>();
                services.AddSpeechProvider<GptSoVitsService, GptSovitsSpeechServiceSettingsControl>();
                // 天气图标模板
                var defaultWeatherIconTemplateDictionary = new ResourceDictionary()
                {
                    Source = new Uri("pack://application:,,,/ClassIsland;component/Controls/WeatherIcons/DefaultWeatherIconTemplate.xaml")
                };
                services.AddWeatherIconTemplate("classisland.weatherIcons.materialDesign", "默认（图标集）",
                    (DataTemplate)defaultWeatherIconTemplateDictionary["DefaultWeatherIconTemplate"]!);
                var simpleTextWeatherIconTemplateDictionary = new ResourceDictionary()
                {
                    Source = new Uri("pack://application:,,,/ClassIsland;component/Controls/WeatherIcons/SimpleTextWeatherIconTemplate.xaml")
                };
                services.AddWeatherIconTemplate("classisland.weatherIcons.simpleText", "纯文本",
                    (DataTemplate)simpleTextWeatherIconTemplateDictionary["SimpleTextWeatherIconTemplate"]!);

                // Plugins
                if (!ApplicationCommand.Safe)
                {
                    PluginService.InitializePlugins(context, services);
                }
            }).Build();
        AppBase.CurrentLifetime = ApplicationLifetime.Starting;
        Logger = GetService<ILogger<App>>();
        Logger.LogInformation("ClassIsland {}", AppVersionLong);
        var lifetime = IAppHost.GetService<IHostApplicationLifetime>();
        lifetime.ApplicationStarted.Register(() => Logger.LogInformation("App started."));
        lifetime.ApplicationStopping.Register(() =>
        {
            Logger.LogInformation("App stopping.");
            Stop();
        });
        lifetime.ApplicationStopped.Register(() => Logger.LogInformation("App stopped."));
        lifetime.ApplicationStopping.Register(Stop);
        if (ApplicationCommand.Verbose)
        {
            AppDomain.CurrentDomain.FirstChanceException += (o, args) => Logger.LogTrace(args.Exception, "发生内部异常");
            AppDomain.CurrentDomain.AssemblyLoad += (o, args) => Logger.LogTrace("加载程序集：{} ({})", args.LoadedAssembly.FullName, args.LoadedAssembly.Location);
        }
#if DEBUG
        MemoryProfiler.GetSnapshot("Host built");
        Helpers.MemoryDiagnostics.Log("Host built");
#endif
        CommandManager.RegisterClassCommandBinding(typeof(Window), new CommandBinding(UriNavigationCommands.UriNavigationCommand, UriNavigationCommandExecuted));
        CommandManager.RegisterClassCommandBinding(typeof(Page), new CommandBinding(UriNavigationCommands.UriNavigationCommand, UriNavigationCommandExecuted));
        await GetService<IManagementService>().SetupManagement();
        await GetService<SettingsService>().LoadSettingsAsync();
        Settings = GetService<SettingsService>().Settings;
        Settings.IsSystemSpeechSystemExist = isSystemSpeechSystemExist;
        Settings.IsNetworkConnect = InternetGetConnectedState(out var _);
        Settings.DiagnosticStartupCount++;
        // 记录MLE
        if (ApplicationCommand.PrevSessionMemoryKilled)
        {
            Settings.DiagnosticMemoryKillCount++;
            Settings.DiagnosticLastMemoryKillTime = DateTime.Now;
        }
        //OverrideFocusVisualStyle();
        var threadedUiDispatcherAwaiter =
            AsyncBox.RelatedAsyncDispatchers.GetOrAdd(Dispatcher, dispatcher => UIDispatcher.RunNewAsync("AsyncBox"));
        await Task.Run(() =>
        {
            while (!threadedUiDispatcherAwaiter.IsCompleted)
            {
            }
        });
        ThreadedUiDispatcher = threadedUiDispatcherAwaiter.Result;
        Logger.LogInformation("初始化应用。");

        IThemeService.IsTransientDisabled = Settings.IsTransientDisabled;
        IThemeService.IsWaitForTransientDisabled = Settings.IsWaitForTransientDisabled;
        if (Settings.IsSplashEnabled && !ApplicationCommand.Quiet)
        {
            ThemeService.FreezeApplicationResources();
            ThreadedUiDispatcher.Invoke(() =>
            {
                GetService<SplashWindowBase>().Show();
            });
        }
        GetService<ISplashService>().CurrentProgress = 30;
        GetService<ISplashService>().SetDetailedStatus("正在启动挂起检查服务");

        GetService<IHangService>();

        GetService<ISplashService>().SetDetailedStatus("正在创建任务栏图标");
        try
        {
            GetService<ITaskBarIconService>().MainTaskBarIcon.ForceCreate(false);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "创建任务栏图标失败。");
        }

        GetService<ISplashService>().CurrentProgress = 45;

        GetService<ISplashService>().SetDetailedStatus("正在加载档案");
        await GetService<IProfileService>().LoadProfileAsync();
        GetService<IWeatherService>();
        GetService<IExactTimeService>();
        await GetService<IComponentsService>().LoadManagementConfig();
        _ = GetService<WallpaperPickingService>().GetWallpaperAsync();
        _ = IAppHost.Host.StartAsync();
        IAppHost.GetService<IPluginMarketService>().LoadPluginSource();

        GetService<ThemeApplyService>();
        Logger.LogInformation("正在初始化MainWindow。");
        GetService<ISplashService>().SetDetailedStatus("正在启动主界面所需的服务");
        GetService<ISplashService>().CurrentProgress = 55;
#if DEBUG
        MemoryProfiler.GetSnapshot("Pre MainWindow init");
#endif
        GetService<ISplashService>().CurrentProgress = 80;
        GetService<ISplashService>().SetDetailedStatus("正在初始化主界面（步骤 2/2）");
        // 自绘模式（UseSelfDrawnIsland）下完全不创建 MainWindow，改由 IslandHost 呈现。
        if (!Settings.UseSelfDrawnIsland)
        {
            ShowMainWindow();
        }
        else
        {
            Logger.LogInformation("已启用自绘主界面，跳过 MainWindow 创建。");
        }
        // 自绘主界面宿主（由设置 UseSelfDrawnIsland 控制是否显示）。
        try
        {
            GetService<IslandHost>().Show();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "初始化自绘主界面 IslandHost 失败。");
        }
        if (Settings.UseSelfDrawnIsland)
        {
            // 自绘模式下没有 MainWindow.OnContentRendered 触发启动完成链，这里主动触发。
            CompleteStartup();
        }
        GetService<SettingsService>().Settings.PropertyChanged += OnIslandRenderModeSettingChanged;
        GetService<IWindowRuleService>();
        GetService<SignalTriggerHandlerService>();

        // 注册uri导航
        var uriNavigationService = GetService<IUriNavigationService>();
        uriNavigationService.HandleAppNavigation("test", args => CommonDialog.ShowInfo($"测试导航：{args.Uri}"));
        uriNavigationService.HandleAppNavigation("settings", args => GetService<SettingsWindowNew>().OpenUri(args.Uri));
        uriNavigationService.HandleAppNavigation("profile", args => GetService<ProfileSettingsWindow>().Open());
        uriNavigationService.HandleAppNavigation("helps", args => uriNavigationService.Navigate(new Uri("https://docs.classisland.tech/app/")));
        uriNavigationService.HandleAppNavigation("config-errors", args => GetService<ConfigErrorsWindow>().ShowDialog());

        try
        {
            await App.GetService<FileFolderService>().ProcessAutoBackupAsync();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "无法创建自动备份。");
        }

        if (ApplicationCommand.UpdateDeleteTarget != null)
        {
            GetService<SettingsService>().Settings.LastUpdateStatus = UpdateStatus.UpToDate;
            GetService<ITaskBarIconService>().ShowNotification("更新完成。", $"应用已更新到版本{AppVersion}。点击此处以查看更新日志。", clickedCallback:() => uriNavigationService.NavigateWrapped(new Uri("classisland://app/settings/update")));
        }
    }

    /// <summary>
    /// 完成应用启动流程。可重复调用，但只会实际执行一次。
    /// </summary>
    internal void CompleteStartup()
    {
        if (_startupCompleted)
        {
            return;
        }

        _startupCompleted = true;

        DiagnosticService.EndStartup();
        GetService<ISplashService>().CurrentProgress = 98;
        GetService<ISplashService>().SetDetailedStatus("正在进行启动后操作");
        // 由于在应用启动时调用 WMI 会导致无法使用触摸，故在应用启动完成后再获取设备统计信息。
        // https://github.com/dotnet/wpf/issues/9752
        AppStarted?.Invoke(this, EventArgs.Empty);
        StartUriPipeServer();
        GetService<NotificationDisplayService>();
        GetService<TrayIconService>().Initialize();
        GetService<IUriNavigationService>().HandleAppNavigation("class-swap", args => GetService<TrayIconService>().OpenClassSwapWindow());
        GetService<IConditionPulseService>().NotifyStatusChanged();
        GetService<ScriptRuntimeService>().Initialize();
        File.Delete(Path.Combine(AppRootFolderPath, ".startup-count"));
        if (ConfigureFileHelper.Errors.FirstOrDefault(x => x.Critical) != null)
        {
            GetService<ITaskBarIconService>().ShowNotification("配置文件损坏", "LegacyIsland 部分配置文件已损坏且无法加载，这些配置文件已恢复至默认值。点击此消息以查看详细信息和从过往备份中恢复配置文件。", clickedCallback:() => GetService<IUriNavigationService>().NavigateWrapped(new Uri("classisland://app/config-errors")));
        }
        if (Settings.CorruptPluginsDisabledLastSession)
        {
            Settings.CorruptPluginsDisabledLastSession = false;
            GetService<ITaskBarIconService>().ShowNotification("已自动禁用异常插件", "LegacyIsland 已自动禁用导致上次崩溃的插件。您可以在排除问题后前往【应用设置】->【插件】中重新启用这些插件，或在【应用设置】->【基本】中调整是否自动禁用异常插件。", clickedCallback: () => GetService<IUriNavigationService>().NavigateWrapped(new Uri("classisland://app/settings/classisland.plugins")));
        }
        if (Settings.IsSplashEnabled)
        {
            App.GetService<ISplashService>().EndSplash();
        }
        _isStartedCompleted = true;
        AppBase.CurrentLifetime = ApplicationLifetime.Running;

        if (!string.IsNullOrWhiteSpace(ApplicationCommand.Uri))
        {
            try
            {
                GetService<IUriNavigationService>().NavigateWrapped(new Uri(ApplicationCommand.Uri));
            }
            catch (Exception)
            {
                // ignored
            }
        }

        Helpers.MemoryDiagnostics.Log("startup complete");
    }

    /// <summary>创建并显示主窗口（惰性：自绘模式下启动时不创建，之后切回 XAML 模式再按需补建）。</summary>
    private void ShowMainWindow()
    {
        var mw = GetService<MainWindow>();
        if (!_mainWindowInitialized)
        {
            _mainWindowInitialized = true;
            MainWindow = mw;
            mw.StartupCompleted += (_, _) => CompleteStartup();
        }

        mw.Show();
        Helpers.MemoryDiagnostics.Log("MainWindow shown");
    }

    private void OnIslandRenderModeSettingChanged(object? sender,
        System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Settings.UseSelfDrawnIsland) && !Settings.UseSelfDrawnIsland)
        {
            ShowMainWindow();
        }
    }

    private ISpeechService GetSpeechService(IServiceProvider provider)
    {
        try
        {
            var service = IAppHost.Host?.Services.GetKeyedService<ISpeechService>(Settings.SelectedSpeechProvider);
            if (service == null)
            {
                throw new InvalidOperationException($"语音提供方 {Settings.SelectedSpeechProvider} 未注册");
            }
            return service;
        }
        catch (Exception e)
        {
            Logger?.LogError(e, "无法初始化语音提供方 {}", Settings.SelectedSpeechProvider);
        }
        return new BlankSpeechService();
        
    }

    private TopmostEffectWindow BuildTopmostEffectWindow(IServiceProvider x)
    {
        var dispatcher = (Settings.NotificationUseStandaloneEffectUiThread ? ThreadedUiDispatcher : Dispatcher) 
                         ?? Dispatcher;
        return dispatcher.Invoke(() =>
        {
            var window = new TopmostEffectWindow(x.GetRequiredService<ILogger<TopmostEffectWindow>>(), x.GetRequiredService<SettingsService>());
            return window;
        });
        
    }

    private void UriNavigationCommandExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        var uri = "";
        if (e.Parameter is string uriRaw)
        {
            uri = uriRaw;
        }

        //if (sender is Hyperlink hyperlink)
        //{
        //    uri = hyperlink.GetHref();
        //}
        try
        {
            IAppHost.GetService<IUriNavigationService>().NavigateWrapped(new Uri(uri));
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "无法导航到 {}", uri);
            CommonDialog.ShowError($"无法导航到 {uri}：{ex.Message}");
        }
    }

    private void StartUriPipeServer()
    {
        var dispatcher = Dispatcher;
        _ = Task.Run(() =>
        {
            while (!dispatcher.HasShutdownStarted)
            {
                NamedPipeServerStream? server = null;
                try
                {
                    server = new NamedPipeServerStream(
                        "ClassIsland.Uri",
                        PipeDirection.In,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);
                    server.WaitForConnection();
                    string? line;
                    using (var reader = new StreamReader(
                               server,
                               new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                               detectEncodingFromByteOrderMarks: false,
                               bufferSize: 1024,
                               leaveOpen: true))
                    {
                        line = reader.ReadLine();
                    }

                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    var uriText = line;
                    dispatcher.Invoke(() =>
                    {
                        var navigation = IAppHost.TryGetService<IUriNavigationService>();
                        if (navigation == null)
                        {
                            return;
                        }

                        navigation.NavigateWrapped(new Uri(uriText));
                    });
                }
                catch (Exception ex)
                {
                    Logger?.LogDebug(ex, "无法接收第二实例 Uri。");
                    Thread.Sleep(200);
                }
                finally
                {
                    try
                    {
                        server?.Dispose();
                    }
                    catch
                    {
                        // ignored
                    }
                }
            }
        });
    }

    private void ProcessInstanceExisted()
    {
        InstanceExistedWindow popup = new();
        bool needRestart = popup.ShowDialog() ?? false;
        if (!needRestart)
        {
            return;
        }
        try
        {
            var proc = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(FrameworkCompat.ProcessPath)).Where(x=>x.Id != FrameworkCompat.ProcessId);
            foreach (var i in proc)
            {
                i.Kill();
            }

            Process.Start(new ProcessStartInfo(FrameworkCompat.ProcessPath)
            {
                Arguments = FrameworkCompat.JoinArguments("-m")
            });
        }
        catch (Exception e)
        {
            CommonDialog.ShowError($"无法重新启动应用，可能当前运行的实例正在以管理员身份运行。请使用任务管理器终止正在运行的实例，然后再试一次。\n\n{e.Message}");
        }
    }

    private sealed class BindingFailureTraceListener : TraceListener
    {
        private readonly App _app;

        public BindingFailureTraceListener(App app)
        {
            _app = app;
        }

        public override void Write(string message)
        {
        }

        public override void WriteLine(string message)
        {
            WriteMessage(TraceEventType.Warning, message);
        }

        public override void TraceEvent(TraceEventCache eventCache, string source, TraceEventType eventType, int id, string message)
        {
            WriteMessage(eventType, message);
        }

        public override void TraceEvent(TraceEventCache eventCache, string source, TraceEventType eventType, int id, string format, params object[] args)
        {
            var message = args == null || args.Length == 0 ? format : string.Format(CultureInfo.InvariantCulture, format, args);
            WriteMessage(eventType, message);
        }

        private void WriteMessage(TraceEventType eventType, string message)
        {
            if (eventType == TraceEventType.Verbose)
            {
                _app.Logger?.LogTrace(message);
            }
            else
            {
                _app.Logger?.LogWarning(message);
            }
        }
    }

    public static void ReleaseLock()
    {
        var app = (App)Application.Current;
        app.Mutex?.ReleaseMutex();
    }

    public override void Stop()
    {
        if (CurrentLifetime == ApplicationLifetime.Stopping)
        {
            return;
        }
        Dispatcher.Invoke(async () =>
        {
            CurrentLifetime = ApplicationLifetime.Stopping;
            Logger?.LogInformation("正在停止应用");
            AppStopping?.Invoke(this, EventArgs.Empty);
            IAppHost.Host?.Services.GetService<ILessonsService>()?.StopMainTimer();
            GetService<ScriptRuntimeService>()?.Shutdown();
            IAppHost.Host?.StopAsync(TimeSpan.FromSeconds(5));
            IAppHost.Host?.Services.GetService<SettingsService>()?.SaveSettings("停止当前应用程序。");
            IAppHost.Host?.Services.GetService<IProfileService>()?.SaveProfile();
            Current.Shutdown();
            if (AsyncBox.RelatedAsyncDispatchers.TryGetValue(Dispatcher, out var asyncDispatcherAwaiter))
            {
                var asyncDispatcher = await asyncDispatcherAwaiter;
                if (!asyncDispatcher.HasShutdownStarted)
                {
                    asyncDispatcher.InvokeShutdown();
                }
            }
            try
            {
                //ReleaseLock();
            }
            catch (Exception e)
            {
                Logger?.LogError(e, "无法释放 Mutex。");
            }
        });
    }

    public override bool IsAssetsTrimmed() => IsAssetsTrimmedInternal;
    public override event EventHandler? AppStarted;
    public override event EventHandler? AppStopping;

    public override void Restart(bool quiet=false)
    {
        if (quiet)
        {
            Restart(["-m", "-q"]);
        }
        else
        {
            Restart(["-m"]);
        }
        
    }

    public override void Restart(string[] parameters)
    {
        Stop();
        var replaced = FrameworkCompat.ProcessPath.Replace(".dll", ".exe");
        var startInfo = new ProcessStartInfo(replaced)
        {
            Arguments = FrameworkCompat.JoinArguments(parameters)
        };
        Process.Start(startInfo);
    }
}
