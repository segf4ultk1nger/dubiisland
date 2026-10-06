using System;
using Windows.Win32.UI.Accessibility;
using ClassIsland.Core.Abstractions.Services;
using Microsoft.Extensions.Logging;
using System.Windows.Threading;
using System.Collections.Generic;
using System.Diagnostics;
using ClassIsland.Core.Helpers.Native;
using ClassIsland.Core;
using ClassIsland.ViewModels;

namespace ClassIsland.Services;

public class WindowRuleService : IWindowRuleService
{
    public ILogger<WindowRuleService> Logger { get; }
    public IConditionPulseService ConditionPulseService { get; }

    public event WINEVENTPROC? ForegroundWindowChanged;
    public HWND ForegroundHwnd { get; set; }

    private List<HWINEVENTHOOK> _hooks = new();

    private bool _isMoving = false;

    public WindowRuleService(ILogger<WindowRuleService> logger, IConditionPulseService conditionPulseService)
    {
        Logger = logger;
        ConditionPulseService = conditionPulseService;
        eventProc = PfnWinEventProc;
        uint[] events = [EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_MOVESIZEEND, EVENT_SYSTEM_MOVESIZESTART,
            EVENT_SYSTEM_MINIMIZEEND, EVENT_OBJECT_LOCATIONCHANGE];
        foreach (var i in events)
        {
            var flags = WINEVENT_OUTOFCONTEXT;
            if (i == EVENT_OBJECT_LOCATIONCHANGE)
            {
                flags |= WINEVENT_SKIPOWNPROCESS;
            }
            var hook = SetWinEventHook(
                i, i,
                HMODULE.Null, eventProc,
                0, 0,
                flags);
            _hooks.Add(hook);
        }
        ForegroundWindowChanged += ((_, _, _, _, _, _, _) => ConditionPulseService.NotifyStatusChanged());
    }

    ~WindowRuleService()
    {
        foreach (var i in _hooks)
        {
            UnhookWinEvent(i);
        }
    }

    // ReSharper disable once PrivateFieldCanBeConvertedToLocalVariable
    private static WINEVENTPROC eventProc;
    private void PfnWinEventProc(HWINEVENTHOOK hook, uint @event, HWND hwnd, int idObject, int child, uint thread, uint time)
    {
        if (hwnd != GetForegroundWindow())
        {
            return;
        }

        if (@event == EVENT_OBJECT_LOCATIONCHANGE && _isMoving)
        {
            return;
        }

        _isMoving = @event switch
        {
            EVENT_SYSTEM_MOVESIZESTART => true,
            EVENT_SYSTEM_MOVESIZEEND => false,
            _ => _isMoving
        };

        ForegroundHwnd = GetForegroundWindow();
        //Logger.LogTrace("Window event: {} HWND:{} {}", @event, hwnd, hook.Value);
        _ = Dispatcher.CurrentDispatcher.InvokeAsync(() =>
        {
            ForegroundWindowChanged?.Invoke(hook, @event, hwnd, idObject, child, thread, time);
        });
    }

    public unsafe bool IsForegroundWindowClassIsland()
    {
        uint pid = 0;
        GetWindowThreadProcessId(ForegroundHwnd, &pid);
        var process = Process.GetProcessById((int)pid);
        return process.Id == FrameworkCompat.ProcessId;
    }
}