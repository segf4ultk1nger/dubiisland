using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using ClassIsland.Views;
using ClassIsland.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace ClassIsland.Services;

/// <summary>
/// 工具窗口（应用设置、档案编辑、应用日志，不含主窗口）的生命周期管理。
/// 这些窗口原本是 DI 单例且关闭只隐藏，会被 DI 永久持有；此管理器让它们按
/// <see cref="Models.Settings.ToolWindowDestructionPolicy"/> 在闲置（隐藏）一段时间后真正关闭销毁以释放内存，
/// 下次打开时重建。窗口自身仍保持「关闭 = 隐藏」的交互。
/// </summary>
public sealed class ToolWindowManager : IDisposable
{
    private readonly IServiceProvider _services;
    private readonly SettingsService _settingsService;
    private readonly Dictionary<Type, Window> _live = new();
    private readonly HashSet<Window> _destroying = new();
    private readonly Dictionary<Window, DateTime> _hiddenSince = new();
    private readonly DispatcherTimer _timer;

    public ToolWindowManager(IServiceProvider services, SettingsService settingsService)
    {
        _services = services;
        _settingsService = settingsService;
        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _timer.Tick += (_, _) => Sweep();
        _timer.Start();
    }

    /// <summary>获取指定工具窗口的存活实例；已销毁（或被空闲回收）则新建。</summary>
    public T Get<T>() where T : Window
    {
        if (_live.TryGetValue(typeof(T), out var existing))
            return (T)existing;

        var window = Create<T>();
        _live[typeof(T)] = window;
        window.Closing += OnWindowClosing;
        window.Closed += OnWindowClosed;
        window.IsVisibleChanged += OnWindowVisibleChanged;
        Helpers.MemoryDiagnostics.Log($"toolwindow created: {typeof(T).Name}");
        return window;
    }

    /// <summary>当前仍存活（未销毁）的工具窗口类型集合，供诊断使用。</summary>
    public System.Collections.Generic.ICollection<Type> LiveWindowTypes => _live.Keys;

    private T Create<T>() where T : Window
    {
        var window = ActivatorUtilities.CreateInstance<T>(_services);
        if (window is ProfileSettingsWindow profile)
            profile.MainViewModel = App.GetService<MainViewModel>();
        return window;
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        // 窗口自身的 Closing 会把 e.Cancel 置 true 只隐藏；管理器要求销毁时（订阅更晚，最后写入生效）放行。
        if (sender is Window window && _destroying.Contains(window))
            e.Cancel = false;
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (sender is not Window window)
            return;
        _live.Remove(window.GetType());
        _hiddenSince.Remove(window);
        _destroying.Remove(window);
    }

    private void OnWindowVisibleChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not Window window)
            return;
        if (window.IsVisible)
            _hiddenSince.Remove(window); // 重新显示 → 重置闲置计时
        else
            _hiddenSince[window] = DateTime.UtcNow;
    }

    private void Sweep()
    {
        var delay = GetIdleDelaySeconds();
        if (delay is null)
            return;

        var now = DateTime.UtcNow;
        foreach (var pair in new List<KeyValuePair<Window, DateTime>>(_hiddenSince))
        {
            if (_destroying.Contains(pair.Key))
                continue;
            if ((now - pair.Value).TotalSeconds < delay.Value)
                continue;
            Destroy(pair.Key);
        }
    }

    private void Destroy(Window window)
    {
        _destroying.Add(window);
        try
        {
            window.Close();
        }
        finally
        {
            _destroying.Remove(window);
        }

        Helpers.MemoryDiagnostics.Log($"toolwindow destroyed: {window.GetType().Name}");
    }

    /// <summary>当前策略下的闲置销毁阈值（秒）；null = 永不销毁。</summary>
    private double? GetIdleDelaySeconds() => _settingsService.Settings.ToolWindowDestructionPolicy switch
    {
        1 => 3600,
        2 => 1800,
        3 => 900,
        4 => 600,
        5 => 300,
        6 => 180,
        7 => 60,
        8 => 30,
        9 => 0,
        10 => _settingsService.Settings.ToolWindowDestructionCustomSeconds,
        _ => null
    };

    public void Dispose() => _timer.Stop();
}
