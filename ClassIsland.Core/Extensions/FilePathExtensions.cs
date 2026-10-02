using System.IO;

namespace ClassIsland.Core.Extensions;

/// <summary>
/// 文件路径拓展
/// </summary>
public static class FilePathExtensions
{
    /// <summary>
    /// 将路径转换为友好路径
    /// </summary>
    /// <param name="path">磁盘、目录或文件路径</param>
    /// <returns>分区(A:) > 文件夹 > 文件.txt</returns>
    public static string ToFriendlyPath(this string path)
    {
        path = DriveInfo.GetDrives()
            .Where(x => x.DriveType == DriveType.Fixed)
            .Aggregate(path, (current, drive) => current.Replace(drive.Name, $"{drive.VolumeLabel} ({drive.Name.Substring(0, drive.Name.Length - 1)}) > "));
        path = path.Replace(@"\", " > ").Replace("/", " > ");
        if (path.EndsWith(" > "))
            path = path.Substring(0, path.Length - 3);
        return path;
    }
}