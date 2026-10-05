using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Abstractions.Services.SpeechService;
using ClassIsland.Core.Models.Notification;
using ClassIsland.Models.EventArgs;
using ClassIsland.Shared.Abstraction.Models;
using ClassIsland.Shared.Interfaces;
using ClassIsland.Shared.Models.Notification;
using ClassIsland.ViewModels;
using Microsoft.Extensions.Logging;

using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Application = System.Windows.Application;

namespace ClassIsland.Services;

/// <summary>
/// 提醒显示管线。负责提醒队列的排空、音效、语音、<see cref="MainViewModel"/> 提醒状态与动画事件，
/// 与具体界面外壳（<see cref="MainWindow"/> 或自绘岛）解耦，视觉相关操作通过 <see cref="INotificationVisualHost"/> 回调。
/// </summary>
public class NotificationDisplayService
{
    private readonly MainViewModel _viewModel;
    private readonly INotificationHostService _notificationHostService;
    private readonly ILessonsService _lessonsService;
    private readonly IExactTimeService _exactTimeService;
    private readonly ISpeechService _speechService;
    private readonly SettingsService _settingsService;
    private readonly ILogger<NotificationDisplayService> _logger;

    public INotificationVisualHost? VisualHost { get; set; }

    public event EventHandler<MainWindowAnimationEventArgs>? AnimationEvent;

    public NotificationDisplayService(
        MainViewModel viewModel,
        INotificationHostService notificationHostService,
        ILessonsService lessonsService,
        IExactTimeService exactTimeService,
        ISpeechService speechService,
        SettingsService settingsService,
        ILogger<NotificationDisplayService> logger)
    {
        _viewModel = viewModel;
        _notificationHostService = notificationHostService;
        _lessonsService = lessonsService;
        _exactTimeService = exactTimeService;
        _speechService = speechService;
        _settingsService = settingsService;
        _logger = logger;

        _lessonsService.PostMainTimerTicked += LessonsServiceOnPostMainTimerTicked;
    }

    private async void LessonsServiceOnPostMainTimerTicked(object? sender, EventArgs e)
    {
        // 处理提醒请求队列
        await ProcessNotification();
    }

    private void BeginStoryboardInLine(string name)
    {
        _viewModel.LastStoryboardName = name;
        AnimationEvent?.Invoke(this, new MainWindowAnimationEventArgs(name));
    }

    private void PreProcessNotificationContent(NotificationContent content)
    {
        if (content.EndTime != null)  // 如果目标结束时间为空，那么就计算持续时间
        {
            var rawTime = content.EndTime.Value - _exactTimeService.GetCurrentLocalDateTime();
            content.Duration = rawTime > TimeSpan.Zero ? rawTime : TimeSpan.Zero;
        }
    }

    private async Task ProcessNotification()
    {
        var viewModel = _viewModel;
        if (viewModel.IsOverlayOpened)
        {
            return;
        }
        viewModel.IsOverlayOpened = true;  // 上锁

        var notificationsShowed = false;

        if (viewModel.FirstProcessNotifications == DateTime.MinValue)
            viewModel.FirstProcessNotifications = _exactTimeService.GetCurrentLocalDateTime();
        if (!viewModel.Settings.IsNotificationEnabled ||
            (_exactTimeService.GetCurrentLocalDateTime() - viewModel.FirstProcessNotifications <= TimeSpan.FromSeconds(10) &&
             App.ApplicationCommand.Quiet) // 静默启动
           )
        {
            _notificationHostService.RequestQueue.Clear();
        }

        while (_notificationHostService.RequestQueue.Count > 0)
        {
            using var player = new DirectSoundOut();
            var request = viewModel.CurrentNotificationRequest = _notificationHostService.GetRequest();  // 获取当前的通知请求
            INotificationSettings settings = viewModel.Settings;
            foreach (var i in new List<NotificationSettings?>([request.ChannelSettings, request.ProviderSettings, request.RequestNotificationSettings]).OfType<NotificationSettings>().Where(i => i.IsSettingsEnabled))
            {
                settings = i;
                break;
            }
            var mask = request.MaskContent;
            var overlay = request.OverlayContent;
            var isMaskSpeechEnabled = settings.IsSpeechEnabled && request.MaskContent.IsSpeechEnabled && viewModel.Settings.AllowNotificationSpeech;
            var isOverlaySpeechEnabled = request.OverlayContent != null && settings.IsSpeechEnabled && request.OverlayContent.IsSpeechEnabled && viewModel.Settings.AllowNotificationSpeech;
            _logger.LogInformation("处理通知请求：{} {}", request.MaskContent.GetType(), request.OverlayContent?.GetType());
            var cancellationToken = request.CancellationTokenSource.Token;

            PreProcessNotificationContent(mask);


            if (request.MaskContent.Duration > TimeSpan.Zero && !cancellationToken.IsCancellationRequested)
            {
                notificationsShowed = true;
                viewModel.CurrentMaskContent = request.MaskContent;  // 加载Mask元素
                viewModel.IsNotificationWindowExplicitShowed = settings.IsNotificationTopmostEnabled && viewModel.Settings.AllowNotificationTopmost;
                if (viewModel.IsNotificationWindowExplicitShowed && viewModel.Settings.WindowLayer == 0)  // 如果处于置底状态，还需要激活窗口来强制显示窗口。
                {
                    VisualHost?.OnNotificationTopmostChanged();
                }

                if (isMaskSpeechEnabled)
                {
                    _speechService.EnqueueSpeechQueue(request.MaskContent.SpeechContent);
                }
                BeginStoryboardInLine("OverlayMaskIn");
                // 播放提醒音效
                if (settings.IsNotificationSoundEnabled && viewModel.Settings.AllowNotificationSound)
                {
                    try
                    {
                        var provider = string.IsNullOrWhiteSpace(settings.NotificationSoundPath)
                            ? new StreamMediaFoundationReader(
                                Application.GetResourceStream(INotificationProvider.DefaultNotificationSoundUri)!.Stream).ToSampleProvider()
                            : new AudioFileReader(settings.NotificationSoundPath);
                        var volume = new VolumeSampleProvider(provider)
                        {
                            Volume = (float)_settingsService.Settings.NotificationSoundVolume
                        };
                        player.Init(volume);
                        player.Play();
                    }
                    catch (Exception e)
                    {
                        _logger.LogError(e, "无法播放提醒音效：{}", settings.NotificationSoundPath);
                    }
                }
                // 播放提醒特效
                if (settings.IsNotificationEffectEnabled && viewModel.Settings.AllowNotificationEffect)
                {
                    VisualHost?.OnNotificationEffectRequested();
                }

                if (!cancellationToken.IsCancellationRequested)
                {
                    await Task.Run(() => cancellationToken.WaitHandle.WaitOne(request.MaskContent.Duration), cancellationToken);
                }
                if (overlay is null || cancellationToken.IsCancellationRequested || overlay.Duration <= TimeSpan.Zero)
                {
                    BeginStoryboardInLine("OverlayMaskOutDirect");
                }
                else
                {
                    PreProcessNotificationContent(overlay);
                    viewModel.CurrentOverlayContent = overlay;
                    if (isOverlaySpeechEnabled)
                    {
                        _speechService.EnqueueSpeechQueue(overlay.SpeechContent);
                    }
                    BeginStoryboardInLine("OverlayMaskOut");
                    viewModel.OverlayRemainStopwatch.Restart();
                    // 倒计时动画
                    VisualHost?.OnNotificationProgressStarted(overlay.Duration);
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        await Task.Run(() => cancellationToken.WaitHandle.WaitOne(overlay.Duration),
                            cancellationToken);
                    }
                    VisualHost?.OnNotificationProgressStopped();
                }
                _speechService.ClearSpeechQueue();
            }

            if (_notificationHostService.RequestQueue.Count < 1 && notificationsShowed)
            {
                BeginStoryboardInLine("OverlayOut");
            }
            request.CompletedTokenSource.Cancel();
        }

        viewModel.CurrentOverlayContent = null;
        viewModel.CurrentMaskContent = null;
        viewModel.IsOverlayOpened = false;
        if (viewModel.IsNotificationWindowExplicitShowed)
        {
            viewModel.IsNotificationWindowExplicitShowed = false;
            VisualHost?.OnNotificationTopmostChanged();
        }
    }
}
