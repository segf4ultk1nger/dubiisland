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
        ReloadFontFamilies();
    }

    public void ReloadFontFamilies()
    {
        var fonts = new List<FontFamily>(Fonts.SystemFontFamilies);
        try
        {
            if (Directory.Exists(FontsFolderPath))
            {
                fonts.AddRange(Fonts.GetFontFamilies(FontsFolderPath));
            }
        }
        catch
        {
            // 目录里放了损坏字体等情况，忽略即可，不影响系统字体列表。
        }

        FontFamilies.Clear();
        foreach (var font in fonts)
        {
            FontFamilies.Add(font);
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
            ReloadFontFamilies();
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
