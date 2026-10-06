using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Models.EventArgs;
using System;

namespace ClassIsland.Services.Scripting;

/// <summary>
/// 脚本 URI 触发服务。仅向新脚本运行时转发 <c>on.uri</c> 的触发与恢复事件。
/// </summary>
public class UriTriggerHandlerService
{
    private IUriNavigationService UriNavigationService { get; }

    internal event EventHandler<UriTriggerHandledEventArgs>? HandledRun;
    internal event EventHandler<UriTriggerHandledEventArgs>? HandledRevert;

    public UriTriggerHandlerService(IUriNavigationService uriNavigationService)
    {
        UriNavigationService = uriNavigationService;

        UriNavigationService.HandleAppNavigation("api/automation/run", args =>
        {
            HandledRun?.Invoke(this, new UriTriggerHandledEventArgs(string.Join("/", args.ChildrenPathPatterns)));
        });
        UriNavigationService.HandleAppNavigation("api/automation/revert", args =>
        {
            HandledRevert?.Invoke(this, new UriTriggerHandledEventArgs(string.Join("/", args.ChildrenPathPatterns)));
        });
    }
}
