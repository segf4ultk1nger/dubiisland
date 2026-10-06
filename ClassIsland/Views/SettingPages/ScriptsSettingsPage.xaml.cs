using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Enums.SettingsWindow;
using ClassIsland.Services;
using ClassIsland.Services.Scripting;
using ClassIsland.ViewModels.SettingsPages;
using Microsoft.Extensions.Logging;

namespace ClassIsland.Views.SettingPages;

/// <summary>
/// ScriptsSettingsPage.xaml 的交互逻辑。
/// </summary>
[SettingsPageInfo("scripts", "脚本", SettingsPageCategory.Internal)]
public partial class ScriptsSettingsPage
{
    private const string ScriptTemplate = "// 在此编写脚本。\non.startup(function () {\n    log(\"新脚本已加载\");\n});\n";

    public ScriptsSettingsViewModel ViewModel { get; } = new();
    public ScriptRuntimeService Runtime { get; }
    public SettingsService SettingsService { get; }
    public ILogger<ScriptsSettingsPage> Logger { get; }

    private string? _pendingSelectFile;
    private string? _loadedFile;

    public ScriptsSettingsPage(ScriptRuntimeService runtime, SettingsService settingsService,
        ILogger<ScriptsSettingsPage> logger)
    {
        Runtime = runtime;
        SettingsService = settingsService;
        Logger = logger;
        DataContext = this;
        InitializeComponent();

        Runtime.ScriptsChanged += OnScriptsChanged;
        Runtime.ScriptLog += OnScriptLog;
        Unloaded += OnUnloaded;

        RefreshDocuments();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Runtime.ScriptsChanged -= OnScriptsChanged;
        Runtime.ScriptLog -= OnScriptLog;
    }

    private void OnScriptsChanged(object? sender, EventArgs e) => RefreshDocuments();

    private void OnScriptLog(object? sender, ScriptLogEntry entry)
    {
        ViewModel.Logs.Add(entry);
        while (ViewModel.Logs.Count > 500)
        {
            ViewModel.Logs.RemoveAt(0);
        }
    }

    private void RefreshDocuments()
    {
        var previousFile = ViewModel.SelectedDocument?.FileName;
        ViewModel.Documents.Clear();
        foreach (var document in Runtime.Documents)
        {
            ViewModel.Documents.Add(document);
        }

        ScriptDocument? selected = null;
        if (_pendingSelectFile != null)
        {
            selected = FindDocument(_pendingSelectFile);
            _pendingSelectFile = null;
        }

        selected ??= FindDocument(previousFile);
        selected ??= ViewModel.Documents.FirstOrDefault();

        ViewModel.SelectedDocument = selected;
        ViewModel.IsPanelOpened = selected != null;
    }

    private ScriptDocument? FindDocument(string? fileName)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            return null;
        }

        return ViewModel.Documents.FirstOrDefault(d =>
            string.Equals(d.FileName, fileName, StringComparison.OrdinalIgnoreCase));
    }

    private void ListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0)
        {
            ViewModel.IsPanelOpened = false;
            return;
        }

        ViewModel.IsPanelOpened = true;
        var document = ViewModel.SelectedDocument;
        if (document != null && !string.Equals(document.FileName, _loadedFile, StringComparison.OrdinalIgnoreCase))
        {
            LoadEditor(document);
        }
    }

    private void LoadEditor(ScriptDocument? document)
    {
        _loadedFile = document?.FileName;
        ViewModel.EditorText = document != null && File.Exists(document.FilePath)
            ? File.ReadAllText(document.FilePath)
            : "";
    }

    private void ButtonNewScript_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(ScriptRuntimeService.ScriptDirectory);
            var index = 1;
            string path;
            do
            {
                path = Path.Combine(ScriptRuntimeService.ScriptDirectory, $"script-{index}.js");
                index++;
            } while (File.Exists(path));

            File.WriteAllText(path, ScriptTemplate);
            _pendingSelectFile = Path.GetFileName(path);
            Runtime.Reload(_pendingSelectFile);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "新建脚本失败。");
        }
    }

    private void ButtonOpenDirectory_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(ScriptRuntimeService.ScriptDirectory);
            Process.Start(new ProcessStartInfo(ScriptRuntimeService.ScriptDirectory) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "打开脚本目录失败。");
        }
    }

    private void ButtonRefresh_OnClick(object sender, RoutedEventArgs e) => Runtime.ReloadAll();

    private void ScriptEnabled_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { DataContext: ScriptDocument document } checkBox)
        {
            Runtime.SetEnabled(document.FileName, checkBox.IsChecked == true);
        }
    }

    private void ButtonSave_OnClick(object sender, RoutedEventArgs e)
    {
        var document = ViewModel.SelectedDocument;
        if (document == null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(ScriptRuntimeService.ScriptDirectory);
            File.WriteAllText(document.FilePath, ViewModel.EditorText ?? "");
            Runtime.Reload(document.FileName);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "保存脚本“{File}”失败。", document.FileName);
        }
    }

    private void ButtonReload_OnClick(object sender, RoutedEventArgs e)
    {
        var document = ViewModel.SelectedDocument;
        if (document == null)
        {
            return;
        }

        Runtime.Reload(document.FileName);
        LoadEditor(document);
    }

    private void ButtonRun_OnClick(object sender, RoutedEventArgs e)
    {
        var document = ViewModel.SelectedDocument;
        if (document != null)
        {
            Runtime.RunDebug(document.FileName);
        }
    }

    private void ButtonRecover_OnClick(object sender, RoutedEventArgs e)
    {
        var document = ViewModel.SelectedDocument;
        if (document != null)
        {
            Runtime.RecoverDebug(document.FileName);
        }
    }

    private void ButtonClearLog_OnClick(object sender, RoutedEventArgs e) => ViewModel.Logs.Clear();

    private void NameTextBox_OnLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox && ViewModel.SelectedDocument != null)
        {
            Runtime.SetName(ViewModel.SelectedDocument.FileName, textBox.Text);
        }
    }
}
