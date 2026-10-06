using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ClassIsland.Core;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Controls;
using ClassIsland.Core.Controls.Ruleset;
using ClassIsland.Core.Controls.Scripting;
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
        SettingsService settingsService)
    {
        SettingsService = settingsService;
        ComponentsService = componentsService;
        InitializeComponent();

        DataContext = this;
        if (FindResource("DataProxy") is BindingProxy proxy)
        {
            proxy.Data = this;
        }
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
        ViewModel.IsCompact = RootGrid.ActualWidth > 0 && RootGrid.ActualWidth < CompactWidthThreshold;
        // 紧凑模式下若已有选中项（页面被缓存复用），回来时直接显示其设置，避免「树里还高亮着却没有属性面板」。
        ViewModel.IsTreeVisible = !ViewModel.IsCompact || ViewModel.SelectedNode == null;
        _layoutReady = true;
        ApplyTreeLayout();
        SchedulePrewarmComponentSettings();
    }

    /// <summary>空闲时预热所有组件的设置控件，避免首次选中时套模板卡顿。</summary>
    private void SchedulePrewarmComponentSettings()
    {
        var all = ComponentsService.CurrentComponents.Lines
            .SelectMany(line => line.Children ?? Enumerable.Empty<ComponentSettings>());
        foreach (var component in EnumerateComponentSettings(all))
        {
            var target = component;
            Dispatcher.BeginInvoke(new System.Action(() => ComponentPresenter.Prewarm(target, PrewarmHost)),
                DispatcherPriority.ApplicationIdle);
        }
    }

    private static IEnumerable<ComponentSettings> EnumerateComponentSettings(IEnumerable<ComponentSettings> source)
    {
        foreach (var component in source)
        {
            yield return component;
            if (component.Children != null)
            {
                foreach (var child in EnumerateComponentSettings(component.Children))
                {
                    yield return child;
                }
            }
        }
    }

    private void ComponentsSettingsPage_OnUnloaded(object sender, RoutedEventArgs e)
    {
        SettingsService.Settings.PropertyChanged -= OnSettingsOnPropertyChanged;
    }

    private void TreeComponents_OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        ViewModel.SelectedNode = e.NewValue;
        // 拖拽排序落点后的选中（gong 会补选落点项）不应再触发收树。
        if (_suppressCompactHide)
        {
            return;
        }
        // 紧凑模式下鼠标按下会先于拖拽阈值触发选中；此时立即收树会把拖拽源抽走，导致无法拖拽排序。
        // 先挂起，等真正抬起（没在拖拽）时再收。
        if (ViewModel.IsCompact && ViewModel.IsTreeVisible && Mouse.LeftButton == MouseButtonState.Pressed)
        {
            _deferredCompactHide = true;
            return;
        }
        HideTreeIfCompact();
    }

    private void TreeComponents_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        => _suppressCompactHide = false;

    private void TreeComponents_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_suppressCompactHide)
        {
            // 刚拖拽排序完，这次抬手不算点击，保留组件树。
            _deferredCompactHide = false;
            return;
        }
        if (_deferredCompactHide)
        {
            _deferredCompactHide = false;
            HideTreeIfCompact();
            return;
        }
        // 点击已选中项不会再触发 SelectedItemChanged，这里补一次收树；点空白处不收。
        if (ViewModel.IsCompact && ViewModel.IsTreeVisible &&
            ComponentTreeHitTest.GetItem(TreeComponents, e.GetPosition(TreeComponents)) != null)
        {
            HideTreeIfCompact();
        }
    }

    /// <summary>行背景铺满整宽，缩进由层级换算的 Padding 模拟。</summary>
    private void ComponentTreeItem_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TreeViewItem item)
            return;
        var depth = 0;
        for (var parent = ItemsControl.ItemsControlFromItemContainer(item);
             parent is TreeViewItem parentItem;
             parent = ItemsControl.ItemsControlFromItemContainer(parentItem))
        {
            depth++;
        }

        item.Padding = new Thickness(8 + depth * 18, 0, 8, 0);
    }

    /// <summary>把 ListBox 截获的滚轮事件转给外层 ScrollViewer，避免鼠标悬停在对齐按钮上时无法滚动页面。</summary>
    private void UIElement_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled)
            return;
        e.Handled = true;
        var scrollViewer = FindAncestorScrollViewer(sender as DependencyObject);
        scrollViewer?.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta / 3.0);
    }

    private static ScrollViewer? FindAncestorScrollViewer(DependencyObject? d)
    {
        while (d != null)
        {
            if (d is ScrollViewer scrollViewer)
                return scrollViewer;
            d = VisualTreeHelper.GetParent(d);
        }

        return null;
    }

    private void ContainerComponentsSource_OnFilter(object sender, FilterEventArgs e)
    {
        if (e.Item is not ComponentInfo info)
        {
            return;
        }

        e.Accepted = info.IsComponentContainer;
    }

    private void NonContainerComponentsSource_OnFilter(object sender, FilterEventArgs e)
    {
        if (e.Item is not ComponentInfo info)
        {
            return;
        }

        e.Accepted = !info.IsComponentContainer;
    }

    private void ButtonMore_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || FindResource("MoreMenu") is not ContextMenu menu)
            return;
        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void TreeComponents_OnMouseMove(object sender, MouseEventArgs e)
    {
        // 命中整行（含行内留白），而不是只看文字那一小块。
        var item = ComponentTreeHitTest.GetItem(TreeComponents, e.GetPosition(TreeComponents));
        Preview.HoveredNode = item?.DataContext;
    }

    private void TreeComponents_OnMouseLeave(object sender, MouseEventArgs e) => Preview.HoveredNode = null;

    #region 紧凑模式

    private const double CompactWidthThreshold = 660;

    private bool _layoutReady;
    private bool _deferredCompactHide;
    private bool _suppressCompactHide;

    /// <summary>页面过窄时进入紧凑模式：组件树与属性面板互斥全宽。</summary>
    private void RootGrid_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width <= 0)
            return;
        var compact = e.NewSize.Width < CompactWidthThreshold;
        if (_layoutReady && ViewModel.IsCompact == compact)
            return;
        ViewModel.IsCompact = compact;
        // 进入紧凑模式时：已有选中项就直接显示其设置，否则显示组件树。
        ViewModel.IsTreeVisible = !compact || ViewModel.SelectedNode == null;
        _layoutReady = true;
        ApplyTreeLayout();
    }

    private void ButtonToggleTree_OnClick(object sender, RoutedEventArgs e)
    {
        // 紧凑模式下树与属性面板互斥：从属性面板返回树时清空选中，避免树里残留高亮。
        if (ViewModel.IsCompact && !ViewModel.IsTreeVisible && ViewModel.SelectedNode is { } selected)
        {
            if (FindContainer(TreeComponents, selected) is { } container)
            {
                container.IsSelected = false;
            }
            ViewModel.SelectedNode = null;
        }
        ViewModel.IsTreeVisible = !ViewModel.IsTreeVisible;
        ApplyTreeLayout();
    }

    private void ApplyTreeLayout()
    {
        var showTree = ViewModel.IsTreeVisible;
        var treeFullWidth = showTree && ViewModel.IsCompact;

        if (!showTree)
        {
            TreeColumn.MinWidth = 0;
            TreeColumn.Width = new GridLength(0);
            SplitterColumn.Width = new GridLength(0);
            Splitter.Visibility = Visibility.Collapsed;
        }
        else if (treeFullWidth)
        {
            // 紧凑模式：树与详情互斥全宽。
            TreeColumn.MinWidth = 0;
            TreeColumn.Width = new GridLength(1, GridUnitType.Star);
            SplitterColumn.Width = new GridLength(0);
            Splitter.Visibility = Visibility.Collapsed;
        }
        else
        {
            TreeColumn.MinWidth = 180;
            TreeColumn.Width = new GridLength(230);
            SplitterColumn.Width = GridLength.Auto;
            Splitter.Visibility = Visibility.Visible;
        }

        DetailColumn.MinWidth = treeFullWidth ? 0 : 320;
        DetailColumn.Width = treeFullWidth ? new GridLength(0) : new GridLength(1, GridUnitType.Star);

        TreePanel.Visibility = showTree ? Visibility.Visible : Visibility.Collapsed;
    }

    private void HideTreeIfCompact()
    {
        if (ViewModel.IsCompact && ViewModel.IsTreeVisible)
        {
            ViewModel.IsTreeVisible = false;
            ApplyTreeLayout();
        }
    }

    #endregion

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
            FindResource("ConditionEditor") is not ConditionEditor control)
        {
            return;
        }
        control.SetBinding(ConditionEditor.ConditionProperty,
            new Binding(nameof(ComponentSettings.HideCondition)) { Source = component, Mode = BindingMode.TwoWay });
        OpenDrawer("ConditionEditor");
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
    /// <param name="revealDetails">紧凑模式下是否让出组件树去显示属性面板；拖拽排序时应为 false，避免刚排完就跳进设置。</param>
    private void SetSelectedNode(object? node, bool revealDetails = true)
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
        if (revealDetails)
        {
            HideTreeIfCompact();
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
        // 拖拽排序结束后留在组件树里继续拖，而不是跳进属性面板；并抑制落点补选触发的收树。
        _deferredCompactHide = false;
        _suppressCompactHide = true;
        SetSelectedNode(settings, revealDetails: false);
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
