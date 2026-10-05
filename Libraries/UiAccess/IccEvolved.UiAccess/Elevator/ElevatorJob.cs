using IccEvolved.UiAccess.Process;
using IccEvolved.UiAccess.Tokens;

namespace IccEvolved.UiAccess.Elevator;

/// <summary>一次提权作业的参数（在 SYSTEM 上下文内执行）。</summary>
internal sealed class ElevatorJobArgs
{
    public string Desktop;
    public bool EnableAllPrivilegesOnTarget;
    public uint ExtraCreateFlags;
    public bool FromService; // true = 服务进程（SYSTEM）执行 → CreateProcessAsUserW
    public string FullCommandLine; // 含 exe 与 marker
    public bool LoadProfile = true;
    public bool NewConsole;
    public int SessionId;
    public string TargetExe;
    public TargetTokenMode TargetMode;
    public int TargetPid;
    public bool UiAccess = true;
}

internal sealed class ElevatorJobResult
{
    public int ChildPid;
    public int Error;
    public bool Success;
}

/// <summary>
///     提权作业本体：建目标令牌 → 打 TokenUIAccess 标 → 创建进程。
///     调用方必须处于 SYSTEM 上下文（模拟 winlogon / 服务进程 / SYSTEM 用户），
///     否则 SetTokenInformation(TokenUIAccess) 会因缺 SeTcbPrivilege 失败。
/// </summary>
internal static class ElevatorJob
{
    public static ElevatorJobResult Execute(ElevatorJobArgs a)
    {
        var result = new ElevatorJobResult();
        SafeKernelHandle token = null;
        try
        {
            int error;
            token = TargetTokenFactory.Create(a.TargetMode, a.TargetPid, a.SessionId, out error);
            if (token == null)
            {
                result.Error = error;
                return result;
            }

            if (a.UiAccess)
                if (!TokenUtil.SetUiAccess(token, true, out error))
                {
                    result.Error = error;
                    return result;
                }

            if (a.EnableAllPrivilegesOnTarget)
                if (!TokenUtil.EnableAllPrivileges(token, out error))
                {
                    result.Error = error;
                    return result;
                }

            int pid, cerr;
            if (!ProcessStarter.Create(token, a.FullCommandLine, a.FromService, a.LoadProfile,
                    a.NewConsole, a.ExtraCreateFlags, a.Desktop, out pid, out cerr))
            {
                result.Error = cerr;
                return result;
            }

            result.Success = true;
            result.ChildPid = pid;
            return result;
        }
        finally
        {
            token?.Dispose();
        }
    }
}