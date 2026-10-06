using ClassIsland.Core.Models.Components;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClassIsland.ViewModels.SettingsPages;

public class ComponentsSettingsViewModel : ObservableRecipient
{
    private object? _selectedNode;
    private object? _contextMenuTarget;
    private string _createProfileName = "";

    /// <summary>
    /// 当前在组件树中选中的节点，可能是 <see cref="MainWindowLineSettings"/> 或 <see cref="ComponentSettings"/>。
    /// </summary>
    public object? SelectedNode
    {
        get => _selectedNode;
        set
        {
            if (Equals(value, _selectedNode)) return;
            _selectedNode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedComponent));
            OnPropertyChanged(nameof(SelectedLine));
        }
    }

    /// <summary>
    /// 当前选中的组件（若选中的是行，则为 null）。
    /// </summary>
    public ComponentSettings? SelectedComponent => _selectedNode as ComponentSettings;

    /// <summary>
    /// 当前选中的行（若选中的是组件，则为 null）。
    /// </summary>
    public MainWindowLineSettings? SelectedLine => _selectedNode as MainWindowLineSettings;

    /// <summary>
    /// 右键菜单打开时对应的目标节点。
    /// </summary>
    public object? ContextMenuTarget
    {
        get => _contextMenuTarget;
        set
        {
            if (Equals(value, _contextMenuTarget)) return;
            _contextMenuTarget = value;
            OnPropertyChanged();
        }
    }

    public string CreateProfileName
    {
        get => _createProfileName;
        set
        {
            if (value == _createProfileName) return;
            _createProfileName = value;
            OnPropertyChanged();
        }
    }
}
