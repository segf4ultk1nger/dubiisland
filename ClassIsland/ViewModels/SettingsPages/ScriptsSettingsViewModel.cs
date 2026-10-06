using System.Collections.ObjectModel;
using ClassIsland.Services.Scripting;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClassIsland.ViewModels.SettingsPages;

/// <summary>
/// 脚本设置页面的视图模型。
/// </summary>
public class ScriptsSettingsViewModel : ObservableRecipient
{
    private bool _isPanelOpened;
    private ScriptDocument? _selectedDocument;
    private string _editorText = "";

    /// <summary>
    /// 脚本文档列表。
    /// </summary>
    public ObservableCollection<ScriptDocument> Documents { get; } = new();

    /// <summary>
    /// 日志列表。
    /// </summary>
    public ObservableCollection<ScriptLogEntry> Logs { get; } = new();

    /// <summary>
    /// 当前选中的脚本文档。
    /// </summary>
    public ScriptDocument? SelectedDocument
    {
        get => _selectedDocument;
        set
        {
            if (value == _selectedDocument) return;
            _selectedDocument = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 编辑器中的脚本内容。
    /// </summary>
    public string EditorText
    {
        get => _editorText;
        set
        {
            if (value == _editorText) return;
            _editorText = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 是否已打开右侧详情面板（压缩模式下控制列表/详情切换）。
    /// </summary>
    public bool IsPanelOpened
    {
        get => _isPanelOpened;
        set
        {
            if (value == _isPanelOpened) return;
            _isPanelOpened = value;
            OnPropertyChanged();
        }
    }
}
