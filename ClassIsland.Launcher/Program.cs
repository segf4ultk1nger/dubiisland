using System.Diagnostics;
using System.Reflection;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace ClassIsland.Launcher;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var root = Path.GetFullPath(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "");
        var installation = Directory.GetDirectories(root)
            .Where(x => Path.GetFileName(x).StartsWith("app") && !File.Exists(Path.Combine(x, ".destroy")))
            .OrderBy(x => File.Exists(Path.Combine(x, ".current")) ? 1 : 0)
            .ThenBy(x =>
            {
                var filename = Path.GetFileName(x);
                var split = filename.Split('-');
                if (split.Length <= 1)
                {
                    return new Version();
                }

                return Version.TryParse(split[1], out var version) ? version : new Version();
            })
            .FirstOrDefault();

        if (installation == null || !File.Exists(Path.Combine(installation, "ClassIsland.exe")))
        {
            PInvoke.MessageBox(HWND.Null, "找不到有效的 ClassIsland 版本，可能是安装已损坏。请在 https://classisland.tech/download 重新下载并安装 ClassIsland。",
                "ClassIsland", MESSAGEBOX_STYLE.MB_APPLMODAL | MESSAGEBOX_STYLE.MB_ICONSTOP);
            return 1;
        }

        var startInfo = new ProcessStartInfo()
        {
            FileName = Path.Combine(Path.Combine(installation, "ClassIsland.exe")),
            WorkingDirectory = root
        };
        startInfo.Arguments = JoinArguments(args);
        Process.Start(startInfo);

        return 0;
    }

    private static string JoinArguments(string[] args)
    {
        return string.Join(" ", args.Select(QuoteArgument));
    }

    private static string QuoteArgument(string arg)
    {
        if (arg.Length != 0 && arg.IndexOf(' ') < 0 && arg.IndexOf('\t') < 0 && arg.IndexOf('"') < 0)
        {
            return arg;
        }

        var escaped = arg.Replace("\"", "\\\"");
        var trailingSlashes = 0;
        for (var i = arg.Length - 1; i >= 0 && arg[i] == '\\'; i--)
        {
            trailingSlashes++;
        }

        return "\"" + escaped + new string('\\', trailingSlashes) + "\"";
    }
}
