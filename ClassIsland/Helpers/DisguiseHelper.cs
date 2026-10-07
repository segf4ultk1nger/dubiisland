using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ClassIsland.Helpers;

public static class DisguiseHelper
{
    private const string LeadingChars = "abcdefghijklmnopqrstuvwxyz";
    private const string NameChars = "abcdefghijklmnopqrstuvwxyz0123456789";

    /// <summary>
    /// 生成一个首字符为字母的 10 位小写字母数字随机名，避免与真实系统进程重名。
    /// </summary>
    public static string GenerateRandomName()
    {
        var buffer = new byte[10];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(buffer);
        }

        var builder = new StringBuilder(buffer.Length);
        builder.Append(LeadingChars[buffer[0] % LeadingChars.Length]);
        for (var i = 1; i < buffer.Length; i++)
        {
            builder.Append(NameChars[buffer[i] % NameChars.Length]);
        }

        return builder.ToString();
    }

    /// <summary>
    /// 校验伪装进程名是否为不含路径分隔符的合法文件名。
    /// </summary>
    public static bool IsValidProcessName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        return name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && name == Path.GetFileName(name);
    }
}
