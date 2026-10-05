using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace IccEvolved.UiAccess.Process;

/// <summary>重启自身时的 marker 参数与交接文件逻辑（对应 C++ 版的 superTop_wait.signal）。</summary>
internal static class SelfRelauncher
{
    /// <summary>判断命令行参数里是否带 marker（子进程特征），并提取交接文件路径。</summary>
    public static bool IsChildProcess(string[] args, string markerArg, out string handoffPath)
    {
        handoffPath = null;
        if (string.IsNullOrEmpty(markerArg)) return false;
        var prefix = markerArg + "=";
        foreach (var a in args)
        {
            if (a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                handoffPath = a.Substring(prefix.Length);
                return true;
            }

            if (string.Equals(a, markerArg, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>构造追加给子进程的命令行（含 exe 与 marker）。</summary>
    public static string BuildFullCommandLine(string targetExe, string args, string markerArg, string handoffPath,
        bool relaunchSelf)
    {
        var sb = new StringBuilder();
        sb.Append(Quote(targetExe));
        if (!string.IsNullOrEmpty(args))
            sb.Append(' ').Append(args);
        if (relaunchSelf)
            sb.Append(' ').Append(Quote(markerArg + "=" + handoffPath));
        return sb.ToString();
    }

    /// <summary>命令行参数加引号（简单规则，覆盖含空格场景）。</summary>
    public static string Quote(string s)
    {
        if (string.IsNullOrEmpty(s)) return "\"\"";
        if (s.IndexOf(' ') < 0 && s.IndexOf('"') < 0 && s.IndexOf('\t') < 0) return s;
        return "\"" + s.Replace("\"", "\\\"") + "\"";
    }

    /// <summary>写交接文件（子进程已创建成功）。</summary>
    public static void WriteHandoff(string signalPath)
    {
        if (string.IsNullOrEmpty(signalPath)) return;
        try
        {
            var dir = Path.GetDirectoryName(signalPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(signalPath, DateTime.UtcNow.ToString("o"));
        }
        catch
        {
        }
    }

    /// <summary>等待交接文件出现，最多 timeoutMs。</summary>
    public static bool WaitForHandoff(string signalPath, int timeoutMs)
    {
        if (string.IsNullOrEmpty(signalPath)) return false;
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (File.Exists(signalPath)) return true;
            Thread.Sleep(50);
        }

        return File.Exists(signalPath);
    }
}