using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace IccEvolved.UiAccess;

/// <summary>SYSTEM 身份的获取方式（谁能通过 SeTcbPrivilege 检查来设置 TokenUIAccess）。</summary>
public enum SystemIdentityMethod
{
    /// <summary>当前进程已经具备 SeTcbPrivilege（运行在 SYSTEM 上下文），直接使用。</summary>
    CurrentIfSystem = 0,

    /// <summary>偷同会话 winlogon.exe 的令牌，线程模拟成 SYSTEM 后打标。</summary>
    Winlogon,

    /// <summary>创建一次性 Windows 服务，由服务进程（SYSTEM）执行打标与启动。</summary>
    Service
}

/// <summary>最终进程的令牌来源（决定最终进程的权限）。</summary>
public enum TargetTokenMode
{
    /// <summary>按 PID 复制指定进程的令牌（默认 = 当前进程，保持原身份）。</summary>
    SelfPid = 0,

    /// <summary>复制当前（提权中间人）进程的令牌。</summary>
    SelfCurrent,

    /// <summary>会话用户的完整令牌（WTSQueryUserToken + TokenLinkedToken，管理员权限）。仅限 SYSTEM 上下文。</summary>
    SessionFull,

    /// <summary>会话用户的过滤令牌（WTSQueryUserToken，普通受限权限）。仅限 SYSTEM 上下文。</summary>
    SessionFiltered,

    /// <summary>从 explorer.exe/ctfmon.exe 获取降权（非特权）用户令牌。</summary>
    ExplorerDeElevated
}

/// <summary>启动模式。</summary>
public enum LaunchMode
{
    /// <summary>重启当前进程（追加 marker 参数，旧进程通过交接文件确认后退出）。</summary>
    RelaunchSelf = 0,

    /// <summary>启动任意目标程序，不追加 marker。</summary>
    LaunchTarget
}

/// <summary>UIAccess 启动的可配置项。</summary>
public sealed class UiAccessOptions
{
    /// <summary>目标进程的窗口站/桌面（服务路径默认 "winsta0\\default"；winlogon 路径传空则继承调用方桌面）。</summary>
    public string Desktop;

    /// <summary>是否在创建进程前启用目标令牌的全部特权（RunUIAccess 的做法，默认关）。</summary>
    public bool EnableAllPrivilegesOnTarget;

    /// <summary>总开关。false 时 Launch 直接返回 <see cref="UiAccessStatus.NotEnabled" />。</summary>
    public bool Enabled = true;

    /// <summary>额外创建标志（CREATE_SUSPENDED 等），与内置标志按位或。</summary>
    public uint ExtraCreateFlags;

    /// <summary>交接文件路径。helper/中间人创建子进程成功后写此文件，旧进程看到后退出。</summary>
    public string HandoffSignal;

    /// <summary>helper 可执行文件路径；留空则探测库/应用目录下的 IccEvolved.UiAccess.Helper.exe。</summary>
    public string HelperExePath;

    /// <summary>启动模式。</summary>
    public LaunchMode LaunchMode = LaunchMode.RelaunchSelf;

    /// <summary>是否加载用户配置（winlogon 路径 = LOGON_WITH_PROFILE；服务路径 = CreateEnvironmentBlock）。</summary>
    public bool LoadProfile = true;

    /// <summary>relaunchSelf 模式下追加到子进程命令行的 marker 参数（含交接文件路径，如 -uiaccess-child=C:\...）。</summary>
    public string MarkerArg = "-uiaccess-child";

    /// <summary>为目标进程创建新控制台。</summary>
    public bool NewConsole;

    /// <summary>会话 ID；为空时自动从 TargetPid/当前进程推导。</summary>
    public int? SessionId;

    /// <summary>SYSTEM 身份来源链，按顺序尝试，第一个成功即采用。</summary>
    public List<SystemIdentityMethod> SystemIdentityChain = new()
    {
        SystemIdentityMethod.Winlogon,
        SystemIdentityMethod.Service
    };

    /// <summary>目标程序命令行参数（不含 exe 本身）；relaunchSelf 模式留空则继承当前命令行。</summary>
    public string TargetCmdLine;

    /// <summary>LaunchTarget 模式的目标程序路径；relaunchSelf 模式留空则用当前进程。</summary>
    public string TargetExe;

    /// <summary>TargetTokenMode == SelfPid 时使用的目标 PID；为空则用当前进程 PID。</summary>
    public int? TargetPid;

    /// <summary>最终进程的令牌来源。</summary>
    public TargetTokenMode TargetTokenMode = TargetTokenMode.SelfPid;

    /// <summary>是否给目标令牌打 TokenUIAccess 标（true = 超级置顶能力）。</summary>
    public bool UiAccess = true;

    /// <summary>旧进程等待交接文件出现的超时（毫秒）。</summary>
    public int WaitOldExitTimeoutMs = 5000;

    public UiAccessOptions()
    {
        HandoffSignal = Path.Combine(Path.GetTempPath(), "IccUiAccess",
            "handoff-" + Guid.NewGuid().ToString("N") + ".sig");
    }

    /// <summary>配置自检，返回错误描述；null 表示合法。</summary>
    public string Validate()
    {
        if (SystemIdentityChain == null || SystemIdentityChain.Count == 0)
            return "SystemIdentityChain 不能为空";

        var hasSystemContext = SystemIdentityChain.Contains(SystemIdentityMethod.Service)
                               || SystemIdentityChain.Contains(SystemIdentityMethod.CurrentIfSystem);

        if ((TargetTokenMode == TargetTokenMode.SessionFull || TargetTokenMode == TargetTokenMode.SessionFiltered)
            && !hasSystemContext)
            return "目标令牌 SessionFull/SessionFiltered 需要 WTSQueryUserToken（SeTcbPrivilege），"
                   + "SYSTEM 身份链必须包含 Service 或 CurrentIfSystem";

        if (TargetTokenMode == TargetTokenMode.SelfCurrent && SystemIdentityChain.Count == 1
                                                           && SystemIdentityChain[0] == SystemIdentityMethod.Service)
            return "SelfCurrent 令牌来源不能在一次性服务内部使用（服务进程令牌是 SYSTEM）";

        if (LaunchMode == LaunchMode.RelaunchSelf && string.IsNullOrEmpty(MarkerArg))
            return "RelaunchSelf 模式必须提供 MarkerArg";

        if (TargetCmdLine != null && TargetCmdLine.Length > 1000)
            return "TargetCmdLine 过长（服务路径的 IPC 限制为 1000 字符）";

        if (SystemIdentityChain.Contains(SystemIdentityMethod.Service) && !string.IsNullOrEmpty(TargetCmdLine)
                                                                       && TargetCmdLine.Length > 1000)
            return "含 Service 身份时 TargetCmdLine 不能超过 1000 字符（IPC 限制）";

        return null;
    }
}

internal static class OptionsSerialization
{
    /// <summary>序列化为键值行文本（用于传给提权 helper 的作业文件）。</summary>
    public static string Serialize(UiAccessOptions o)
    {
        var sb = new StringBuilder();

        void W(string k, string v)
        {
            sb.Append(k).Append('=').Append(v).Append('\n');
        }

        W("Enabled", o.Enabled ? "1" : "0");
        W("IdentityChain", string.Join(",", o.SystemIdentityChain.Select(m => (int)m).ToArray()));
        W("TargetTokenMode", ((int)o.TargetTokenMode).ToString());
        if (o.TargetPid.HasValue) W("TargetPid", o.TargetPid.Value.ToString());
        W("UiAccess", o.UiAccess ? "1" : "0");
        W("LaunchMode", ((int)o.LaunchMode).ToString());
        W("MarkerArg", ToB64(o.MarkerArg));
        W("HandoffSignal", ToB64(o.HandoffSignal ?? ""));
        W("WaitOldExitTimeoutMs", o.WaitOldExitTimeoutMs.ToString());
        W("LoadProfile", o.LoadProfile ? "1" : "0");
        W("Desktop", ToB64(o.Desktop ?? ""));
        W("NewConsole", o.NewConsole ? "1" : "0");
        W("ExtraCreateFlags", o.ExtraCreateFlags.ToString());
        W("EnableAllPrivilegesOnTarget", o.EnableAllPrivilegesOnTarget ? "1" : "0");
        W("TargetExe", ToB64(o.TargetExe ?? ""));
        W("TargetCmdLine", ToB64(o.TargetCmdLine ?? ""));
        W("HelperExePath", ToB64(o.HelperExePath ?? ""));
        if (o.SessionId.HasValue) W("SessionId", o.SessionId.Value.ToString());
        return sb.ToString();
    }

    /// <summary>反序列化键值行文本。</summary>
    public static UiAccessOptions Deserialize(string text)
    {
        var o = new UiAccessOptions();
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in text.Split('\n'))
        {
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            dict[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
        }

        string G(string k)
        {
            return dict.TryGetValue(k, out var v) ? v : null;
        }

        bool GB(string k, bool def)
        {
            return G(k) == null ? def : G(k) == "1";
        }

        o.Enabled = GB("Enabled", true);
        var chain = G("IdentityChain");
        if (chain != null)
        {
            o.SystemIdentityChain.Clear();
            foreach (var p in chain.Split(','))
                if (int.TryParse(p, out var m) && Enum.IsDefined(typeof(SystemIdentityMethod), m))
                    o.SystemIdentityChain.Add((SystemIdentityMethod)m);
        }

        o.TargetTokenMode = int.TryParse(G("TargetTokenMode"), out var tt) &&
                            Enum.IsDefined(typeof(TargetTokenMode), tt)
            ? (TargetTokenMode)tt
            : TargetTokenMode.SelfPid;
        o.TargetPid = int.TryParse(G("TargetPid"), out var tp) ? tp : null;
        o.UiAccess = GB("UiAccess", true);
        o.LaunchMode = int.TryParse(G("LaunchMode"), out var lm) && Enum.IsDefined(typeof(LaunchMode), lm)
            ? (LaunchMode)lm
            : LaunchMode.RelaunchSelf;
        if (G("MarkerArg") != null) o.MarkerArg = FromB64(G("MarkerArg"));
        if (G("HandoffSignal") != null) o.HandoffSignal = FromB64(G("HandoffSignal"));
        o.WaitOldExitTimeoutMs = int.TryParse(G("WaitOldExitTimeoutMs"), out var wt) ? wt : 5000;
        o.LoadProfile = GB("LoadProfile", true);
        if (G("Desktop") != null) o.Desktop = FromB64(G("Desktop"));
        o.NewConsole = GB("NewConsole", false);
        o.ExtraCreateFlags = uint.TryParse(G("ExtraCreateFlags"), out var ec) ? ec : 0;
        o.EnableAllPrivilegesOnTarget = GB("EnableAllPrivilegesOnTarget", false);
        if (G("TargetExe") != null) o.TargetExe = FromB64(G("TargetExe"));
        if (G("TargetCmdLine") != null) o.TargetCmdLine = FromB64(G("TargetCmdLine"));
        if (G("HelperExePath") != null) o.HelperExePath = FromB64(G("HelperExePath"));
        o.SessionId = int.TryParse(G("SessionId"), out var sd) ? sd : null;
        return o;
    }

    private static string ToB64(string s)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(s ?? ""));
    }

    private static string FromB64(string s)
    {
        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(s));
        }
        catch (FormatException)
        {
            return "";
        }
    }
}