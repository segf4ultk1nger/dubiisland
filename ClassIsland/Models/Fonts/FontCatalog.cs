using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClassIsland.Models.Fonts;

public class FontCatalog
{
    public List<FontCatalogItem> Fonts { get; set; } = new();
}

public partial class FontCatalogItem : ObservableObject
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Family { get; set; } = "";
    public string? Description { get; set; }
    public string? License { get; set; }
    public string? LicenseUrl { get; set; }
    public List<FontCatalogFile> Files { get; set; } = new();

    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
    public bool HasLicense => !string.IsNullOrWhiteSpace(License);

    [ObservableProperty] private bool _isDownloading;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private bool _isInstalled;
    [ObservableProperty] private string? _statusText;
}

public class FontCatalogFile
{
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public string? Sha256 { get; set; }
}
