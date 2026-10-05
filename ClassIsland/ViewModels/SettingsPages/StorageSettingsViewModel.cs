using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using ClassIsland.Helpers;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClassIsland.ViewModels.SettingsPages;

/// <summary>
/// 存储占用比例条的一段。
/// </summary>
public class StorageSegment
{
    public string Name { get; set; } = "";
    public string SizeText { get; set; } = "";
    public ulong Size { get; set; }
    public double Ratio { get; set; }
    public Brush Brush { get; set; } = Brushes.Gray;
}

/// <summary>
/// 可清理的目录。
/// </summary>
public partial class StorageCleanupTarget : ObservableObject
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string Description { get; set; } = "";
    public ulong Size { get; set; }

    [ObservableProperty] private string _sizeText = "…";
    [ObservableProperty] private bool _isSelected = true;
    [ObservableProperty] private bool _isCleaning;
}

public partial class StorageSettingsViewModel : ObservableRecipient
{
    public ObservableCollection<StorageSegment> Categories { get; } = new();

    public ObservableCollection<StorageCleanupTarget> CleanupTargets { get; } = new();

    [ObservableProperty] private bool _isBackingUp;
    [ObservableProperty] private bool _isBackupFinished;
    [ObservableProperty] private bool _isScanningStorage;
    [ObservableProperty] private bool _isCleaning;
    [ObservableProperty] private double _diskUsedRatio;
    [ObservableProperty] private string _appSizeText = "…";
    [ObservableProperty] private string _diskInfoText = "";
    [ObservableProperty] private string _diskNameText = "";
    [ObservableProperty] private string _cleanupSelectedText = "…";

    public string DataFolderPath => Path.GetFullPath(App.AppRootFolderPath);
    public string BackupFolderPath => Path.Combine(DataFolderPath, "Backups");
    public string LogFolderPath => Path.GetFullPath(App.AppLogFolderPath);

    public StorageSettingsViewModel()
    {
        AddCleanupTarget("缓存", App.AppCacheFolderPath, "缓存数据，可安全清理。");
        AddCleanupTarget("日志", App.AppLogFolderPath, "运行日志，可安全清理。");
        AddCleanupTarget("临时", App.AppTempFolderPath, "临时文件，可安全清理。");
    }

    private void AddCleanupTarget(string name, string path, string description)
    {
        var target = new StorageCleanupTarget { Name = name, Path = path, Description = description };
        target.PropertyChanged += OnCleanupTargetPropertyChanged;
        CleanupTargets.Add(target);
    }

    private void OnCleanupTargetPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(StorageCleanupTarget.IsSelected) or nameof(StorageCleanupTarget.SizeText))
        {
            UpdateCleanupSelectedText();
        }
    }

    private void UpdateCleanupSelectedText()
    {
        ulong sum = 0;
        foreach (var target in CleanupTargets)
        {
            if (target.IsSelected)
            {
                sum += target.Size;
            }
        }

        CleanupSelectedText = StorageSizeHelper.FormatSize(sum);
    }

    public async Task RefreshStorageInfoAsync()
    {
        if (IsScanningStorage)
        {
            return;
        }

        IsScanningStorage = true;
        try
        {
            var (categories, diskUsedRatio, appText, diskText, diskNameText) = await Task.Run(BuildStorageInfo);

            Categories.Clear();
            foreach (var category in categories)
            {
                Categories.Add(category);
            }

            DiskUsedRatio = diskUsedRatio;
            AppSizeText = appText;
            DiskInfoText = diskText;
            DiskNameText = diskNameText;

            foreach (var target in CleanupTargets)
            {
                var category = Categories.FirstOrDefault(c => c.Name == target.Name);
                target.Size = category?.Size ?? 0;
                target.SizeText = category?.SizeText ?? StorageSizeHelper.FormatSize(0);
            }
            UpdateCleanupSelectedText();
        }
        catch
        {
            // 扫描失败时保持原值，不打断设置页。
        }
        finally
        {
            IsScanningStorage = false;
        }
    }

    public async Task CleanupSelectedAsync()
    {
        if (IsCleaning)
        {
            return;
        }

        IsCleaning = true;
        try
        {
            foreach (var target in CleanupTargets)
            {
                if (target.IsSelected)
                {
                    await CleanupCoreAsync(target);
                }
            }
            await RefreshStorageInfoAsync();
        }
        finally
        {
            IsCleaning = false;
        }
    }

    private async Task CleanupCoreAsync(StorageCleanupTarget target)
    {
        target.IsCleaning = true;
        target.SizeText = "清理中…";
        try
        {
            await Task.Run(() => DeleteFolderContents(target.Path));
        }
        finally
        {
            target.IsCleaning = false;
        }
    }

    private static void DeleteFolderContents(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        // 尽最大努力删除；正在被占用的文件（如当前日志）跳过即可。
        foreach (var file in Directory.EnumerateFiles(path))
        {
            try
            {
                File.Delete(file);
            }
            catch
            {
                // ignored
            }
        }

        foreach (var directory in Directory.EnumerateDirectories(path))
        {
            try
            {
                Directory.Delete(directory, true);
            }
            catch
            {
                // ignored
            }
        }
    }

    // 笔刷在后台线程创建、UI 线程使用，必须冻结后才能跨线程。
    private static SolidColorBrush MakeBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static (List<StorageSegment> Categories, double DiskUsedRatio, string AppText, string DiskText, string DiskNameText)
        BuildStorageInfo()
    {
        var definitions = new (string Name, string Path, Color Color)[]
        {
            ("档案", Path.Combine(App.AppRootFolderPath, "Profiles"), Color.FromRgb(0x4F, 0x8E, 0xF7)),
            ("配置", App.AppConfigPath, Color.FromRgb(0x34, 0xC7, 0x59)),
            ("备份", Path.Combine(App.AppRootFolderPath, "Backups"), Color.FromRgb(0xFF, 0x9F, 0x0A)),
            ("插件", Path.Combine(App.AppRootFolderPath, "Plugins"), Color.FromRgb(0xBF, 0x5A, 0xF2)),
            ("字体", Path.Combine(App.AppRootFolderPath, "Fonts"), Color.FromRgb(0xFF, 0x37, 0x5F)),
            ("缓存", App.AppCacheFolderPath, Color.FromRgb(0x8E, 0x8E, 0x93)),
            ("日志", App.AppLogFolderPath, Color.FromRgb(0xA2, 0x84, 0x5E)),
            ("临时", App.AppTempFolderPath, Color.FromRgb(0x5A, 0xC8, 0xFA)),
        };

        var sizes = new List<(string Name, ulong Size, Color Color)>();
        ulong appTotal = 0;
        foreach (var definition in definitions)
        {
            var size = StorageSizeHelper.GetFolderStorageSize(definition.Path);
            sizes.Add((definition.Name, size, definition.Color));
            appTotal += size;
        }

        var categories = new List<StorageSegment>();
        foreach (var item in sizes)
        {
            categories.Add(new StorageSegment
            {
                Name = item.Name,
                Size = item.Size,
                SizeText = StorageSizeHelper.FormatSize(item.Size),
                Ratio = appTotal > 0 ? (double)item.Size / appTotal : 0,
                Brush = MakeBrush(item.Color)
            });
        }

        ulong total = 0;
        ulong free = 0;
        var hardDriveName = "";
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(App.AppRootFolderPath))!);
            total = (ulong)drive.TotalSize;
            free = (ulong)drive.AvailableFreeSpace;
            hardDriveName = string.IsNullOrWhiteSpace(drive.VolumeLabel)
                ? drive.Name.TrimEnd('\\')
                : $"{drive.VolumeLabel} ({drive.Name.TrimEnd('\\')})";
        }
        catch
        {
            // 无法获取磁盘信息时退化为仅显示应用占用。
        }

        var used = total > free ? total - free : 0;
        var diskUsedRatio = total > 0 ? (double)used / total : 0;
        var diskText = total > 0
            ? $"磁盘已用 {StorageSizeHelper.FormatSize(used)} / 共 {StorageSizeHelper.FormatSize(total)}（可用 {StorageSizeHelper.FormatSize(free)}）"
            : "无法获取磁盘信息";
        var diskNameText = hardDriveName.Length > 0 ? $"LegacyIsland 在硬盘：{hardDriveName}" : "";

        return (categories, diskUsedRatio, StorageSizeHelper.FormatSize(appTotal), diskText, diskNameText);
    }
}
