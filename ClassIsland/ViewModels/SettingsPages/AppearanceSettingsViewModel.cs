using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using ClassIsland.Helpers;
using ClassIsland.Models.Fonts;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClassIsland.ViewModels.SettingsPages;

public partial class AppearanceSettingsViewModel : ObservableRecipient
{
    public static string FontsFolderPath => FontDownloadHelper.FontsFolderPath;

    public ObservableCollection<FontFamily> FontFamilies { get; } = new();

    public ObservableCollection<FontCatalogItem> FontCatalogItems { get; } = new();

    private readonly List<FontCatalogItem> _allFontItems = new();

    [ObservableProperty] private bool _isFontCatalogLoading;
    [ObservableProperty] private string? _fontCatalogError;
    [ObservableProperty] private string _fontSearchText = "";

    partial void OnFontSearchTextChanged(string value) => ApplyFontFilter();

    private void ApplyFontFilter()
    {
        var keyword = FontSearchText.Trim();
        FontCatalogItems.Clear();
        foreach (var item in _allFontItems)
        {
            if (keyword.Length == 0 ||
                item.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                item.Family.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                FontCatalogItems.Add(item);
            }
        }
    }

    public AppearanceSettingsViewModel()
    {
        _ = ReloadFontFamiliesAsync();
    }

    /// <summary>
    /// 后台线程枚举系统字体（首次枚举较慢），完成后回 UI 线程填充集合，
    /// 避免在 UI 线程上阻塞外观页的创建。
    /// </summary>
    public async Task ReloadFontFamiliesAsync()
    {
        // 在页面/窗口存活时记下 UI 线程的 Dispatcher，供后台结果回来时派发。
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null)
        {
            return;
        }

        List<FontFamily> fonts;
        try
        {
            fonts = await Task.Run(() =>
            {
                var list = new List<FontFamily>(Fonts.SystemFontFamilies);
                try
                {
                    if (Directory.Exists(FontsFolderPath))
                    {
                        list.AddRange(Fonts.GetFontFamilies(FontsFolderPath));
                    }
                }
                catch
                {
                    // 目录里放了损坏字体等情况，忽略即可，不影响系统字体列表。
                }

                return list;
            }).ConfigureAwait(false);
        }
        catch
        {
            return;
        }

        // 窗口/应用可能已经关闭，Dispatcher 正在或已经关闭，此时不要再碰 UI 集合。
        if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
        {
            return;
        }

        try
        {
            await dispatcher.InvokeAsync(() =>
            {
                FontFamilies.Clear();
                foreach (var font in fonts)
                {
                    FontFamilies.Add(font);
                }
            }).Task.ConfigureAwait(false);
        }
        catch
        {
            // 关闭过程中的派发竞态，忽略。
        }
    }

    public async Task LoadFontCatalogAsync()
    {
        IsFontCatalogLoading = true;
        FontCatalogError = null;
        try
        {
            var catalog = await FontDownloadHelper.GetCatalogAsync();
            _allFontItems.Clear();
            foreach (var item in catalog.Fonts)
            {
                item.IsInstalled = FontDownloadHelper.IsInstalled(item);
                _allFontItems.Add(item);
            }
            ApplyFontFilter();
        }
        catch (Exception ex)
        {
            FontCatalogError = $"无法获取字体列表：{ex.Message}";
        }
        finally
        {
            IsFontCatalogLoading = false;
        }
    }

    public async Task DownloadFontAsync(FontCatalogItem item)
    {
        if (item.IsDownloading)
        {
            return;
        }

        item.IsDownloading = true;
        item.Progress = 0;
        item.StatusText = "正在下载…";
        try
        {
            var progress = new Progress<double>(p => item.Progress = p * 100);
            await FontDownloadHelper.DownloadAsync(item, progress);
            item.IsInstalled = true;
            item.StatusText = "已下载";
            await ReloadFontFamiliesAsync();
        }
        catch (Exception ex)
        {
            item.StatusText = $"下载失败：{ex.Message}";
        }
        finally
        {
            item.IsDownloading = false;
        }
    }

    [ObservableProperty] private string _fontPreviewText = LoadFontPreview();

    private static string LoadFontPreview()
    {
        try
        {
            var stream = Application.GetResourceStream(new Uri("/Assets/FontPreview.txt", UriKind.Relative))?.Stream;
            if (stream == null)
            {
                return "";
            }

            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd().TrimEnd('\r', '\n');
        }
        catch
        {
            return "";
        }
    }
}
