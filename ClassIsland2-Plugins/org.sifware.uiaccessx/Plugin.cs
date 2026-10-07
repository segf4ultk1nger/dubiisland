using ClassIsland.Core.Abstractions;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Org.Sifware.UiAccessX;

[PluginEntrance]
public class Plugin : PluginBase
{
    public override void Initialize(HostBuilderContext context, IServiceCollection services)
    {
        UiAccessX.Initialize(PluginConfigFolder);

        // 在应用窗口创建之前尽早尝试以 UIAccess 身份重启自身（成功后本进程退出）。
        // 若已有其它插件提供 UIAccess，则让位，不做 UIA 重启。
        if (!UiAccessX.HasConflictUiAccess)
        {
            UiAccessX.TryRelaunchWithUiAccess();
        }

        // 把设置项注入到内置设置界面中（通用/基本、主界面/窗口、外观/分体主界面）。
        SettingsInjector.Start();

        // 运行期把主界面岛的圆角渲染为超椭圆（若已在设置中开启）。
        Squircle.SquircleRenderer.Start();

        // 按设置屏蔽 Windows 10 触摸边缘手势。
        EdgeGestureUtil.Start();
    }
}
