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
        UiAccessX.TryRelaunchWithUiAccess();

        // 把设置项注入到内置设置界面中（通用/基本、主界面/窗口）。
        SettingsInjector.Start();
    }
}
