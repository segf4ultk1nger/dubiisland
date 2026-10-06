using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using ClassIsland.Core;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Controls;
using ClassIsland.Core.Controls.Ruleset;
using ClassIsland.Core.Enums.SettingsWindow;
using ClassIsland.Core.Models.Components;
using ClassIsland.Core.Services;
using ClassIsland.Services;
using ClassIsland.Shared.Helpers;
using ClassIsland.ViewModels.SettingsPages;
using CommunityToolkit.Mvvm.Input;
using GongSolutions.Wpf.DragDrop;
using Path = System.IO.Path;
using CommonDialog = ClassIsland.Core.Controls.CommonDialog.CommonDialog;

namespace ClassIsland.Views.SettingPages;

/// <summary>
/// ComponentsSettingsPage.xaml 的交互逻辑
/// </summary>
[SettingsPageInfo("components", "组件", IconGlyphs.WidgetsOutline, IconGlyphs.Widgets, SettingsPageCategory.Internal)]
public partial class ComponentsSettingsPage : SettingsPageBase, IDropTarget
{
    public IComponentsService ComponentsService { get; }

    public ComponentsSettingsViewModel ViewModel { get; } = new();

    public SettingsService SettingsService { get; }

    public ComponentsSettingsPage(
        IComponentsService componentsService,
        SettingsService settingsService,
        IThemeService themeService,
        ILessonsService lessonsService,
        IProfileService profileService,
        IExactTimeService exactTimeService,
        IRulesetService rulesetService,
        IWeatherService weatherService)
    {
        SettingsService = settingsService;
        ComponentsService = componentsService;
        InitializeComponent();

        DataContext = this;
        if (FindResource("DataProxy") is BindingProxy proxy)
        {
            proxy.Data = this;
        }

        IslandPreviewHost.Initialize(settingsService, themeService, componentsService, lessonsService, profileService,
            exactTimeService, rulesetService, weatherService);
    }

    private void OnSettingsOnPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(SettingsService.Settings.CurrentComponentConfig))
        {
            ViewModel.SelectedNode = null;
        }
    }

    private void ComponentsSettingsPage_OnLoaded(object sender, RoutedEventArgs e)
    {
        SettingsService.Settings.PropertyChanged += OnSettingsOnPropertyChanged;
    }

    private void ComponentsSettingsPage_OnUnloaded(object sender, RoutedEventArgs e)
    {
        SettingsService.Settings.PropertyChanged -= OnSettingsOnPropertyChanged;
    }

    private void TreeComponents_OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        ViewModel.SelectedNode = e.NewValue;
    }

    private void ContainerComponentsSource_OnFilter(object sender, FilterEventArgs e)
    {
        if (e.Item is not ComponentInfo info)
        {
            return;
        }

        e.Accepted = info.IsComponentContainer;
    }

    #region 配置方案

    private void ButtonRefresh_OnClick(object sender, RoutedEventArgs e)
    {
        ComponentsService.RefreshConfigs();
    }

    private async void ButtonCreateConfig_OnClick(object sender, RoutedEventArgs e)
    {
        ViewModel.CreateProfileName = "";
        if (FindResource("CreateProfileDialog") is not FrameworkElement content)
            return;
        content.DataContext = this;
        var r = await DialogService.ShowAsync(content, DialogHostIdentifier);
        Debug.WriteLine(r);

        var path = Path.Combine(ClassIsland.Services.ComponentsService.ComponentSettingsPath,
            ViewModel.CreateProfileName + ".json");
        if (r == null || File.Exists(path))
        {
            return;
        }
        ConfigureFileHelper.SaveConfig(path, ClassIsland.Services.ComponentsService.DefaultComponentProfile);
        ComponentsService.RefreshConfigs();
        SettingsService.Settings.CurrentComponentConfig = ViewModel.CreateProfileName;
        ViewModel.SelectedNode = null;
    }

    private void ButtonOpenConfigFolder_OnClick(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo()
        {
            FileName = Path.GetFullPath(ClassIsland.Services.ComponentsService.ComponentSettingsPath),
            UseShellExecute = true
        });
    }

    #endregion

    #region 添加组件

    private void ButtonAddComponent_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || FindResource("AddComponentMenu") is not ContextMenu menu)
            return;
        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    [RelayCommand]
    private void AddComponent(ComponentInfo info)
    {
        var list = EnsureInsertList();
        var settings = new ComponentSettings { Id = info.Guid.ToString() };
        if (info.ComponentType?.BaseType != null)
        {
            ClassIsland.Services.ComponentsService.LoadComponentSettings(settings, info.ComponentType.BaseType);
        }
        list.Add(settings);
        SetSelectedNode(settings);
    }

    private ObservableCollection<ComponentSettings> EnsureInsertList()
    {
        if (ViewModel.SelectedComponent?.Children is { } containerChildren)
            return containerChildren;
        if (ViewModel.SelectedLine?.Children is { } lineChildren)
            return lineChildren;
        var last = ComponentsService.CurrentComponents.Lines.LastOrDefault();
        if (last != null)
            return last.Children;
        var newLine = new MainWindowLineSettings();
        ComponentsService.CurrentComponents.Lines.Add(newLine);
        return newLine.Children;
    }

    #endregion

    #region 组件操作

    [RelayCommand]
    private void RemoveComponent(ComponentSettings settings)
    {
        FindContainingList(settings)?.Remove(settings);
        if (ReferenceEquals(ViewModel.SelectedNode, settings))
        {
            ViewModel.SelectedNode = null;
        }
    }

    [RelayCommand]
    private void DuplicateComponent(ComponentSettings settings)
    {
        var list = FindContainingList(settings);
        if (list == null)
            return;
        var copy = ConfigureFileHelper.CopyObject(settings);
        list.Insert(Math.Max(0, list.IndexOf(settings)), copy);
        SetSelectedNode(copy);
    }

    [RelayCommand]
    private void MoveComponentUp(ComponentSettings settings)
    {
        var list = FindContainingList(settings);
        if (list == null)
            return;
        var index = list.IndexOf(settings);
        if (index > 0)
        {
            list.Move(index, index - 1);
        }
    }

    [RelayCommand]
    private void MoveComponentDown(ComponentSettings settings)
    {
        var list = FindContainingList(settings);
        if (list == null)
            return;
        var index = list.IndexOf(settings);
        if (index >= 0 && index < list.Count - 1)
        {
            list.Move(index, index + 1);
        }
    }

    [RelayCommand]
    private void MoveOutOfContainer(ComponentSettings settings)
    {
        var list = FindContainingList(settings);
        var line = FindContainingLine(settings);
        if (list == null || line == null || ReferenceEquals(list, line.Children))
        {
            return;
        }
        list.Remove(settings);
        line.Children.Add(settings);
    }

    [RelayCommand]
    private void CreateContainerComponent(ComponentInfo container)
    {
        if (ViewModel.ContextMenuTarget is not ComponentSettings target)
            return;
        var list = FindContainingList(target);
        if (list == null)
            return;

        var index = Math.Max(0, list.IndexOf(target));
        var containerSettings = new ComponentSettings { Id = container.Guid.ToString() };
        if (container.ComponentType?.BaseType != null)
        {
            containerSettings.Settings =
                ClassIsland.Services.ComponentsService.LoadComponentSettings(containerSettings, container.ComponentType.BaseType);
        }
        list.Insert(index, containerSettings);
        list.Remove(target);
        containerSettings.Children?.Add(target);
        SetSelectedNode(containerSettings);
    }

    #endregion

    #region 行操作

    private void ButtonCreateMainWindowLine_OnClick(object sender, RoutedEventArgs e) => InsertLineAfter(ViewModel.SelectedLine);

    private void MenuItemInsertLine_OnClick(object sender, RoutedEventArgs e) =>
        InsertLineAfter(ViewModel.ContextMenuTarget as MainWindowLineSettings ?? ViewModel.SelectedLine);

    private void InsertLineAfter(MainWindowLineSettings? anchor)
    {
        var lines = ComponentsService.CurrentComponents.Lines;
        var index = anchor == null ? lines.Count : Math.Min(lines.Count, lines.IndexOf(anchor) + 1);
        var line = new MainWindowLineSettings();
        lines.Insert(index, line);
        SetSelectedNode(line);
    }

    private void MenuItemSetMainLine_OnClick(object sender, RoutedEventArgs e)
    {
        if ((ViewModel.ContextMenuTarget as MainWindowLineSettings ?? ViewModel.SelectedLine) is not { } line)
            return;
        SetMainLine(line);
    }

    private void ButtonSetMainLine_OnClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is MainWindowLineSettings line)
        {
            SetMainLine(line);
        }
    }

    private void SetMainLine(MainWindowLineSettings line)
    {
        foreach (var other in ComponentsService.CurrentComponents.Lines)
        {
            other.IsMainLine = ReferenceEquals(other, line);
        }
    }

    private void MenuItemRemoveLine_OnClick(object sender, RoutedEventArgs e)
    {
        if ((ViewModel.ContextMenuTarget as MainWindowLineSettings ?? ViewModel.SelectedLine) is not { } line)
            return;
        var lines = ComponentsService.CurrentComponents.Lines;
        if (lines.Count <= 1)
        {
            CommonDialog.ShowError("至少需要保留 1 个主界面行。");
            return;
        }
        lines.Remove(line);
        if (ReferenceEquals(ViewModel.SelectedNode, line))
        {
            ViewModel.SelectedNode = null;
        }
    }

    private void ButtonOpenRuleset_OnClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ComponentSettings component ||
            FindResource("RulesetControl") is not RulesetControl control)
        {
            return;
        }
        control.Ruleset = component.HidingRules;
        OpenDrawer("RulesetControl");
    }

    #endregion

    #region 右键菜单

    private void ComponentOperationContextMenu_OnOpened(object sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu { PlacementTarget: FrameworkElement { DataContext: ComponentSettings settings } })
        {
            ViewModel.ContextMenuTarget = settings;
        }
    }

    private void LineOperationContextMenu_OnOpened(object sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu { PlacementTarget: FrameworkElement { DataContext: MainWindowLineSettings line } })
        {
            ViewModel.ContextMenuTarget = line;
        }
    }

    #endregion

    #region 辅助

    private ObservableCollection<ComponentSettings>? FindContainingList(ComponentSettings target)
    {
        foreach (var line in ComponentsService.CurrentComponents.Lines)
        {
            var found = FindIn(line.Children, target);
            if (found != null)
                return found;
        }
        return null;
    }

    private static ObservableCollection<ComponentSettings>? FindIn(ObservableCollection<ComponentSettings> list, ComponentSettings target)
    {
        if (list.Contains(target))
            return list;
        foreach (var component in list)
        {
            if (component.Children is { } children)
            {
                var found = FindIn(children, target);
                if (found != null)
                    return found;
            }
        }
        return null;
    }

    private MainWindowLineSettings? FindContainingLine(ComponentSettings target)
    {
        foreach (var line in ComponentsService.CurrentComponents.Lines)
        {
            if (ContainsRecursive(line.Children, target))
                return line;
        }
        return null;
    }

    private static bool ContainsRecursive(ObservableCollection<ComponentSettings> list, ComponentSettings target)
    {
        foreach (var component in list)
        {
            if (ReferenceEquals(component, target))
                return true;
            if (component.Children is { } children && ContainsRecursive(children, target))
                return true;
        }
        return false;
    }

    /// <summary>设置选中节点，并同步树的选中态（否则点击已选中的项不会再触发事件，右侧面板不更新）。</summary>
    private void SetSelectedNode(object? node)
    {
        ViewModel.SelectedNode = node;
        if (node == null)
            return;
        TreeComponents.UpdateLayout();
        if (FindContainer(TreeComponents, node) is { } container)
        {
            container.IsSelected = true;
            container.BringIntoView();
        }
    }

    private static TreeViewItem? FindContainer(ItemsControl parent, object item)
    {
        if (parent.ItemContainerGenerator.ContainerFromItem(item) is TreeViewItem direct)
            return direct;
        foreach (var child in parent.Items)
        {
            if (parent.ItemContainerGenerator.ContainerFromItem(child) is TreeViewItem childContainer &&
                FindContainer(childContainer, item) is { } found)
            {
                return found;
            }
        }
        return null;
    }

    #endregion

    #region 拖拽

    public void DragEnter(IDropInfo dropInfo)
    {
    }

    public void DragLeave(IDropInfo dropInfo)
    {
    }

    public void DragOver(IDropInfo dropInfo)
    {
        if (dropInfo.Data is not ComponentSettings settings) { dropInfo.Effects = DragDropEffects.None; return; }
        var container = GetHoveredItem(dropInfo);
        if (container == null) { dropInfo.Effects = DragDropEffects.None; return; }
        var (list, _) = ComputeInsert(container, GetFraction(container, dropInfo));
        if (list == null || CollectionBelongsToDescendant(list, settings))
        {
            dropInfo.Effects = DragDropEffects.None;
            return;
        }
        dropInfo.DropTargetAdorner = typeof(TreeInsertAdorner);
        dropInfo.Effects = DragDropEffects.Move;
    }

    public void Drop(IDropInfo dropInfo)
    {
        if (dropInfo.Data is not ComponentSettings settings) return;
        var container = GetHoveredItem(dropInfo);
        if (container == null) return;
        var (targetList, insertIndex) = ComputeInsert(container, GetFraction(container, dropInfo));
        var sourceList = FindContainingList(settings);
        if (targetList == null || sourceList == null || CollectionBelongsToDescendant(targetList, settings))
        {
            return;
        }

        if (ReferenceEquals(sourceList, targetList))
        {
            var oldIndex = sourceList.IndexOf(settings);
            if (oldIndex < 0)
                return;
            if (insertIndex > oldIndex)
                insertIndex--;
            insertIndex = Math.Max(0, Math.Min(insertIndex, sourceList.Count - 1));
            if (insertIndex != oldIndex)
            {
                sourceList.Move(oldIndex, insertIndex);
            }
        }
        else
        {
            insertIndex = Math.Max(0, Math.Min(insertIndex, targetList.Count));
            sourceList.Remove(settings);
            targetList.Insert(insertIndex, settings);
        }
        SetSelectedNode(settings);
    }

    /// <summary>
    /// 按光标在悬停项内的纵向位置计算插入目标：上 1/3 插到该项之前，下 1/3 插到之后，
    /// 中间 1/3 落到容器/行内部（追加为子项）；非容器项中间 1/3 视为插到之后。
    /// </summary>
    private (ObservableCollection<ComponentSettings>? List, int Index) ComputeInsert(TreeViewItem container, double fy)
    {
        switch (container.DataContext)
        {
            case MainWindowLineSettings line:
                return (line.Children, fy < 0.5 ? 0 : line.Children.Count);
            case ComponentSettings component:
                if (fy >= 0.33 && fy <= 0.67 && component.Children is { } children)
                {
                    return (children, children.Count);
                }
                var parent = FindContainingList(component);
                if (parent == null)
                {
                    return (null, 0);
                }
                var index = parent.IndexOf(component);
                return (parent, fy < 0.5 ? index : index + 1);
            default:
                return (null, 0);
        }
    }

    private TreeViewItem? GetHoveredItem(IDropInfo dropInfo)
        => ComponentTreeHitTest.GetItem(dropInfo.VisualTarget, dropInfo.DropPosition)
           ?? dropInfo.VisualTargetItem as TreeViewItem;

    private static double GetFraction(TreeViewItem container, IDropInfo dropInfo)
    {
        var height = container.ActualHeight;
        if (height <= 0 || dropInfo.VisualTarget is not { } target)
            return 0.5;
        try
        {
            var top = container.TransformToAncestor(target).Transform(new Point(0, 0)).Y;
            return (dropInfo.DropPosition.Y - top) / height;
        }
        catch
        {
            return 0.5;
        }
    }

    private static bool CollectionBelongsToDescendant(ObservableCollection<ComponentSettings> list, ComponentSettings component)
    {
        if (component.Children is not { } children)
            return false;
        if (ReferenceEquals(children, list))
            return true;
        foreach (var child in children)
        {
            if (CollectionBelongsToDescendant(list, child))
                return true;
        }
        return false;
    }

    #endregion
}
