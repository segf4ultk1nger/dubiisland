using System;
using ClassIsland.Models.EventArgs;

namespace ClassIsland.Services.Scripting;

/// <summary>
/// 脚本信号广播服务。仅保留事件机制，供 <c>on.signal</c> 与脚本 <c>broadcast</c> 使用。
/// </summary>
public class SignalTriggerHandlerService
{
    /// <summary>
    /// 收到信号广播时触发。
    /// </summary>
    public event EventHandler<SignalTriggerEventArgs>? Handled;

    /// <summary>
    /// 广播一个信号。
    /// </summary>
    public void EmitSignal(string name, bool revert)
    {
        Handled?.Invoke(this, new SignalTriggerEventArgs(name, revert));
    }
}
