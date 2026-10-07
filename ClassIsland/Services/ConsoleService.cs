using System;
using System.IO;
using System.Windows;

namespace ClassIsland.Services;

public class ConsoleService
{
    public static string AsciiLogo = "";
    public static HWND ConsoleHWnd { get; private set; }

    public static void InitializeConsole()
    {
#if DEBUG
        if (ConsoleHWnd == IntPtr.Zero)
        {
            AllocConsole();
        }
        ConsoleHWnd = GetConsoleWindow();
        SetWindowText(ConsoleHWnd, "LegacyIsland 输出");
#endif
        PrintAppInfo();
    }

    public static void PrintAppInfo()
    {
        var s = Application.GetResourceStream(new Uri("/Assets/AsciiLogo.txt", UriKind.RelativeOrAbsolute))?.Stream;
        if (s != null)
        {
            AsciiLogo = new StreamReader(s).ReadToEnd();
        }
        Console.WriteLine(AsciiLogo);
        Console.WriteLine($"LegacyIsland {App.AppVersionLong}");
        Console.WriteLine("「钟表的指针周而复始，就像人的困惑、烦恼、软弱…摇摆不停。但最终，人们依旧要前进，就像你的指针，永远落在前方。」");
        Console.WriteLine();
    }

    public ConsoleService()
    {
    }
}