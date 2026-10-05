using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using ClassIsland.Models.Fonts;

namespace ClassIsland.Helpers;

/// <summary>
/// 字体清单拉取与下载。字体统一存放到程序目录的 Fonts 文件夹（与 Plugins 同级），
/// 由 <see cref="ViewModels.SettingsPages.AppearanceSettingsViewModel"/> 扫描后以 base URI 加载，无需安装到系统。
/// </summary>
public static class FontDownloadHelper
{
    // 字体清单地址（托管在 CNB，未认证即可下载）。
    public const string FontCatalogUrl = "https://cnb.cool/dubiousuniverse/legacyisland-data/-/git/raw/main/fonts.json";

    public static readonly string FontsFolderPath = Path.GetFullPath(Path.Combine(App.AppRootFolderPath, "Fonts"));

    private static readonly HttpClient HttpClient = new();

    public static Task<FontCatalog> GetCatalogAsync(CancellationToken cancellationToken = default)
    {
        return WebRequestHelper.GetJson<FontCatalog>(new Uri(FontCatalogUrl), cancellationToken: cancellationToken);
    }

    public static bool IsInstalled(FontCatalogItem item)
    {
        if (item.Files.Count == 0)
        {
            return false;
        }

        return item.Files.TrueForAll(f => File.Exists(Path.Combine(FontsFolderPath, f.Name)));
    }

    public static async Task DownloadAsync(FontCatalogItem item, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(FontsFolderPath);
        var total = Math.Max(1, item.Files.Count);
        for (var i = 0; i < item.Files.Count; i++)
        {
            var file = item.Files[i];
            var target = Path.Combine(FontsFolderPath, file.Name);
            var temp = target + ".download";

            using (var response = await HttpClient.GetAsync(file.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                response.EnsureSuccessStatusCode();
                using var source = await response.Content.ReadAsStreamAsync();
                using var destination = File.Create(temp);
                var buffer = new byte[81920];
                int read;
                while ((read = await source.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                {
                    await destination.WriteAsync(buffer, 0, read, cancellationToken);
                }
            }

            if (!string.IsNullOrWhiteSpace(file.Sha256) &&
                !ComputeSha256(temp).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(temp);
                throw new InvalidDataException($"字体文件 {file.Name} 校验失败。");
            }

            if (File.Exists(target))
            {
                File.Delete(target);
            }
            File.Move(temp, target);

            progress?.Report((i + 1) / (double)total);
        }
    }

    private static string ComputeSha256(string path)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(path);
        return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
    }
}
