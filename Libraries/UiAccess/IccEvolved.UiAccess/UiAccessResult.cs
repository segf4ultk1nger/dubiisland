using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text;

namespace IccEvolved.UiAccess;

/// <summary>Launch 的结果状态。</summary>
public enum UiAccessStatus
{
    /// <summary>配置总开关关闭。</summary>
    NotEnabled = 0,

    /// <summary>当前进程已经具备 UIAccess（无需再次操作）。</summary>
    AlreadyUiAccess,

    /// <summary>子进程已创建，交接完成。</summary>
    Succeeded,

    /// <summary>所有 SYSTEM 身份方法均失败。</summary>
    Failed,

    /// <summary>用户取消了 UAC 提权确认。</summary>
    CancelledByUser,

    /// <summary>helper 提权或启动失败。</summary>
    ElevationFailed,

    /// <summary>等待交接/服务响应超时。</summary>
    TimedOut,

    /// <summary>配置自检不通过。</summary>
    InvalidConfig
}

/// <summary>Launch / RunElevatedJob 的返回结果。</summary>
public sealed class UiAccessResult
{
    public UiAccessResult(UiAccessStatus status, int errorCode = 0, string message = null, int childPid = 0)
    {
        Status = status;
        ErrorCode = errorCode;
        Message = message;
        ChildPid = childPid;
    }

    /// <summary>结果状态。</summary>
    public UiAccessStatus Status { get; set; }

    /// <summary>最后一条 Win32 错误码（GetLastError）。</summary>
    public int ErrorCode { get; set; }

    /// <summary>人类可读错误/说明。</summary>
    public string Message { get; set; }

    /// <summary>成功时新进程的 PID（0 = 未知/不适用）。</summary>
    public int ChildPid { get; set; }

    /// <summary>最终采用的 SYSTEM 身份方法。</summary>
    public SystemIdentityMethod? UsedMethod { get; set; }

    public bool IsSuccess => Status == UiAccessStatus.Succeeded || Status == UiAccessStatus.AlreadyUiAccess;

    public string ErrorText
    {
        get
        {
            if (ErrorCode == 0) return Message ?? Status.ToString();
            string win32 = null;
            try
            {
                win32 = new Win32Exception(ErrorCode).Message;
            }
            catch
            {
            }

            return Message != null ? $"{Message} (Win32 {ErrorCode}: {win32})" : $"Win32 {ErrorCode}: {win32}";
        }
    }

    internal static UiAccessResult Fail(UiAccessStatus status, int error, string message)
    {
        return new UiAccessResult(status, error, message);
    }

    internal static UiAccessResult Ok(int childPid, SystemIdentityMethod? method)
    {
        return new UiAccessResult(UiAccessStatus.Succeeded, childPid: childPid) { UsedMethod = method };
    }

    public override string ToString()
    {
        return IsSuccess ? $"{Status} pid={ChildPid} method={UsedMethod}" : $"{Status}: {ErrorText}";
    }
}

/// <summary>作业文件的读写（配置 → 文本，结果 → 文本）。</summary>
internal static class JobFile
{
    public static string Write(UiAccessOptions options)
    {
        var dir = Path.Combine(Path.GetTempPath(), "IccUiAccess");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "job-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(path, OptionsSerialization.Serialize(options), Encoding.UTF8);
        return path;
    }

    public static UiAccessOptions Read(string path)
    {
        var text = File.ReadAllText(path, Encoding.UTF8);
        return OptionsSerialization.Deserialize(text);
    }

    public static void WriteResult(string path, UiAccessResult result)
    {
        var sb = new StringBuilder();
        sb.Append("status=").Append((int)result.Status).Append('\n');
        sb.Append("error=").Append(result.ErrorCode).Append('\n');
        sb.Append("pid=").Append(result.ChildPid).Append('\n');
        sb.Append("method=").Append(result.UsedMethod == null ? "" : ((int)result.UsedMethod.Value).ToString())
            .Append('\n');
        sb.Append("message=").Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(result.Message ?? ""))).Append('\n');
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    public static UiAccessResult ReadResult(string path)
    {
        if (!File.Exists(path)) return null;
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
        {
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            dict[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
        }

        var status = dict.TryGetValue("status", out var s) && int.TryParse(s, out var st)
            ? (UiAccessStatus)st
            : UiAccessStatus.Failed;
        var error = dict.TryGetValue("error", out var e) && int.TryParse(e, out var er) ? er : 0;
        var pid = dict.TryGetValue("pid", out var p) && int.TryParse(p, out var pd) ? pd : 0;
        SystemIdentityMethod? method = null;
        if (dict.TryGetValue("method", out var m) && int.TryParse(m, out var mv) &&
            Enum.IsDefined(typeof(SystemIdentityMethod), mv))
            method = (SystemIdentityMethod)mv;
        var message = "";
        if (dict.TryGetValue("message", out var msg) && !string.IsNullOrEmpty(msg))
            try
            {
                message = Encoding.UTF8.GetString(Convert.FromBase64String(msg));
            }
            catch
            {
                message = msg;
            }

        return new UiAccessResult(status, error, message, pid) { UsedMethod = method };
    }
}