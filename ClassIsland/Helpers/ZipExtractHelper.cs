using System;
using System.IO;
using System.IO.Compression;

namespace ClassIsland.Helpers;

/// <summary>
/// <see cref="ZipFile.ExtractToDirectory(string, string, bool)"/> is not available on net472.
/// </summary>
public static class ZipExtractHelper
{
    public static void ExtractToDirectory(string sourceArchiveFileName, string destinationDirectoryName, bool overwriteFiles)
    {
        if (!overwriteFiles)
        {
            ZipFile.ExtractToDirectory(sourceArchiveFileName, destinationDirectoryName);
            return;
        }

        Directory.CreateDirectory(destinationDirectoryName);
        var root = Path.GetFullPath(destinationDirectoryName);
        if (root[root.Length - 1] != Path.DirectorySeparatorChar && root[root.Length - 1] != Path.AltDirectorySeparatorChar)
        {
            root += Path.DirectorySeparatorChar;
        }

        using (var archive = ZipFile.OpenRead(sourceArchiveFileName))
        {
            foreach (var entry in archive.Entries)
            {
                var relative = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
                var destPath = Path.GetFullPath(Path.Combine(destinationDirectoryName, relative));
                if (!destPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (string.IsNullOrEmpty(entry.Name))
                {
                    Directory.CreateDirectory(destPath);
                    continue;
                }

                var directory = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                entry.ExtractToFile(destPath, true);
            }
        }
    }
}
