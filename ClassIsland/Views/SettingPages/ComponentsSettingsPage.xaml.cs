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
using ClassIsland.Core.Abstractions.Models;
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

    public ComponentsSettingsPage(IComponentsService componentsService, SettingsService settingsService)
    {
        SettingsService = settingsService;
        ComponentsService = componentsService;
        InitializeComponent();

        DataContext = this;
        if (FindResource("DataProxy") is BindingProxy proxy)
        {
            proxy.Data = this;
        }
        if (FindResource("DataProxyDuplicate") is BindingProxy proxyDuplicate)
        {
            proxyDuplicate.Data = DuplicateComponentCommand;
        }
    }

    private void OnSettingsOnPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(SettingsService.Settings.CurrentComponentConfig))
        {
            ClearSelectedComponents();
        }
    }

    private void ClearSelectedComponents()
    {
        ViewModel.SelectedComponentSettingsMain = null;
        ViewModel.SelectedComponentSettings = null;
        ViewModel.SelectedMainWindowLineSettings = null;
        CloseComponentChildrenView();
        UpdateSettingsVisibility();
    }

    private void CloseComponentChildrenView()
    {
        ViewModel.IsComponentChildrenViewOpen = false;
        ViewModel.ChildrenComponentSettingsNavigationStack.Clear();
        ViewModel.CanChildrenNavigateBack = false;
        ViewModel.SelectedComponentContainerChildren = [];
        ViewModel.SelectedRootComponent = null;
    }

    private void ButtonRemoveSelectedComponent_OnClick(object sender, RoutedEventArgs e)
    {
        var remove = ViewModel.SelectedComponentSettings;
        if (remove == null)
            return;
        if (ViewModel.SelectedComponentSettings == ViewModel.SelectedRootComponent)
        {
            CloseComponentChildrenView();
        }
        ViewModel.SelectedComponentSettings = null;
        if (ViewModel.SelectedComponentSettingsMain != null)
        {
            foreach (var line in ComponentsService.CurrentComponents.Lines)
            {
                if (line.Children.Remove(remove))
                {
                    break;
                }
            }
        } else if (ViewModel.SelectedComponentSettingsChild != null)
        {
            ViewModel.SelectedComponentContainerChildren.Remove(remove);
        }
    }

    private void ButtonRefresh_OnClick(object sender, RoutedEventArgs e)
    {
        ClearSelectedComponents();
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
        ViewModel.SelectedComponentSettings = null;
        UpdateSettingsVisibility();
    }

    private void ButtonOpenConfigFolder_OnClick(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo()
        {
            FileName = Path.GetFullPath(ClassIsland.Services.ComponentsService.ComponentSettingsPath),
            UseShellExecute = true
        });
    }

    private void SelectorComponents_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count <= 0 || e.AddedItems[0] is not ComponentSettings settings)
        {
            UpdateSettingsVisibility();
            return;
        }
        foreach (var listBox in ViewModel.MainWindowLineListBoxCacheReversed.Keys.Where(x => !Equals(x, sender)))
        {
            listBox.SelectedItem = null;
        }
        ListBoxComponentsChildren.SelectedItem = null;
        ViewModel.SelectedComponentSettingsMain = settings;
        ViewModel.SelectedComponentSettings = settings;
        ViewModel.SelectedComponentSettingsChild = null;
        UpdateSettingsVisibility();
    }

    private void ListBoxMainWindowLineSettings_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ListBox { DataContext: MainWindowLineSettings settings } listBox)
        {
            return;
        }
        ViewModel.MainWindowLineListBoxCacheReversed[listBox] = settings;
    }

    private void ListBoxMainWindowLineSettings_OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is ListBox listBox)
        {
            ViewModel.MainWindowLineListBoxCacheReversed.Remove(listBox);
        }
    }

    private void UpdateSettingsVisibility()
    {
        if (ViewModel.SelectedComponentSettings == null)
        {
            ViewModel.IsComponentAdvancedSettingsVisible = false;
            ViewModel.IsComponentSettingsVisible = false;
            ViewModel.SettingsTabControlIndex = 0;
            return;
        }

        ViewModel.IsComponentAdvancedSettingsVisible = true;
        ViewModel.IsSelectedComponentOnRoot =
            ViewModel.SelectedComponentSettings == ViewModel.SelectedComponentSettingsMain;
        if (ViewModel.SelectedComponentSettings.AssociatedComponentInfo.SettingsType == null)
        {
            ViewModel.IsComponentSettingsVisible = false;
            ViewModel.SettingsTabControlIndex = ViewModel.SettingsTabControlIndex == 1 ? 0 : ViewModel.SettingsTabControlIndex;
            return;
        }
        ViewModel.IsComponentSettingsVisible = true;
        ViewModel.SettingsTabControlIndex = ViewModel.SettingsTabControlIndex == 0 ? 1 : ViewModel.SettingsTabControlIndex;
    }

    private void ButtonOpenRuleset_OnClick(object sender, RoutedEventArgs e)
    {
        if (FindResource("RulesetControl") is not RulesetControl control ||
            ViewModel.SelectedComponentSettings == null) 
            return;
        control.Ruleset = ViewModel.SelectedComponentSettings.HidingRules;
        OpenDrawer("RulesetControl");
    }

    private void ButtonShowChildrenComponents_OnClick(object sender, RoutedEventArgs e)
    {
        SetCurrentSelectedComponentContainer(ViewModel.SelectedComponentSettings);
    }

    private void SetCurrentSelectedComponentContainer(ComponentSettings? componentSettings, bool isBack=false)
    {
        if (componentSettings?.AssociatedComponentInfo?.IsComponentContainer != true)
        {
            return;
        }

        var type = componentSettings.AssociatedComponentInfo.ComponentType?.BaseType;
        if (componentSettings.Settings is System.Text.Json.JsonElement && type != null)
        {
            componentSettings.Settings = ClassIsland.Services.ComponentsService.LoadComponentSettings(componentSettings, type);
        }
        if (componentSettings.Settings is not IComponentContainerSettings settings)
        {
            return;
        }
        if (componentSettings == ViewModel.SelectedComponentSettingsMain)
        {
            ViewModel.ChildrenComponentSettingsNavigationStack.Clear();
            ViewModel.SelectedRootComponent = componentSettings;
        } else if (ViewModel.SelectedContainerComponent != null && !isBack)
        {
            ViewModel.ChildrenComponentSettingsNavigationStack.Push(ViewModel.SelectedContainerComponent);
        }
        ViewModel.SelectedComponentContainerChildren = settings.Children;
        ViewModel.SelectedContainerComponent = componentSettings;
        ViewModel.IsComponentChildrenViewOpen = true;
        ViewModel.CanChildrenNavigateBack = ViewModel.ChildrenComponentSettingsNavigationStack.Count >= 1;
    }

    private void SelectorComponentsChildren_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count <= 0 || e.AddedItems[0] is not ComponentSettings settings)
        {
            UpdateSettingsVisibility();
            return;
        }
        foreach (var listBox in ViewModel.MainWindowLineListBoxCacheReversed.Keys.Where(x => !Equals(x, sender)))
        {
            listBox.SelectedItem = null;
        }

        ViewModel.SelectedComponentSettingsChild = settings;
        ViewModel.SelectedComponentSettings = settings;
        ViewModel.SelectedComponentSettingsMain = null;
        UpdateSettingsVisibility();
    }

    private void ButtonChildrenViewClose_OnClick(object sender, RoutedEventArgs e)
    {
        CloseComponentChildrenView();
    }

    private void ButtonNavigateUp_OnClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.ChildrenComponentSettingsNavigationStack.Count == 0)
        {
            return;
        }
        var settings = ViewModel.ChildrenComponentSettingsNavigationStack.Pop();
        SetCurrentSelectedComponentContainer(settings, true);
    }

    private void ButtonCreateMainWindowLine_OnClick(object sender, RoutedEventArgs e)
    {
        var lines = ComponentsService.CurrentComponents.Lines;
        var index = ViewModel.SelectedMainWindowLineSettings == null
            ? lines.Count
            : Math.Max(0, lines.IndexOf(ViewModel.SelectedMainWindowLineSettings) + 1);
        var lineSettings = new MainWindowLineSettings();
        lines.Insert(index, lineSettings);
        ViewModel.SelectedMainWindowLineSettings = lineSettings;
    }

    private void ButtonRemoveSelectedMainWindowLine_OnClick(object sender, RoutedEventArgs e)
    {
        if (ComponentsService.CurrentComponents.Lines.Count <= 1)
        {
            CommonDialog.ShowError("至少需要保留 1 个主界面行。");
            return;
        }

        if (ViewModel.SelectedMainWindowLineSettings != null)
        {
            ComponentsService.CurrentComponents.Lines.Remove(ViewModel.SelectedMainWindowLineSettings);
        }
    }

    private void ToggleButtonIsMainLine_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton button || button.DataContext is not MainWindowLineSettings line)
        {
            return;
        }

        if (button.IsChecked == true)
        {
            foreach (var other in ComponentsService.CurrentComponents.Lines.Where(x => !ReferenceEquals(x, line)))
            {
                other.IsMainLine = false;
            }
        }
        else
        {
            var firstLine = ComponentsService.CurrentComponents.Lines.FirstOrDefault();
            if (firstLine != null)
            {
                firstLine.IsMainLine = true;
            }
            CommonDialog.ShowHint("已将第一行设置为主要行。");
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

    private void ContainerComponentsSource_OnFilter(object sender, FilterEventArgs e)
    {
        if (e.Item is not ComponentInfo info)
        {
            return;
        }

        e.Accepted = info.IsComponentContainer;
    }

    [RelayCommand]
    private void OpenContextMenu(FrameworkElement element)
    {
        if (element.ContextMenu == null)
        {
            return;
        }

        element.ContextMenu.IsOpen = true;
    }

    [RelayCommand]
    private void CreateContainerComponent(ComponentInfo container)
    {
        if (ViewModel.SelectedComponentSettings == null)
        {
            return;
        }

        var selected = ViewModel.SelectedComponentSettings;
        var list = GetSelectedComponentSource(selected);
        var index = list.IndexOf(selected);

        if (index == -1)
        {
            return;
        }

        index = Math.Min(list.Count - 1, index);
        var newComp = new ComponentSettings()
        {
            Id = container.Guid.ToString(),
        };
        if (container.ComponentType?.BaseType != null)
        {
            newComp.Settings =
                ClassIsland.Services.ComponentsService.LoadComponentSettings(newComp, container.ComponentType.BaseType);
        }
        list.Insert(index, newComp);
        if (selected == ViewModel.SelectedComponentSettingsMain)
        {
            ViewModel.SelectedComponentSettingsMain = newComp;
        }
        else
        {
            ViewModel.SelectedComponentSettingsChild = newComp;
        }
        SetCurrentSelectedComponentContainer(newComp);
        list.Remove(selected);
        newComp.Children?.Add(selected);
        ViewModel.SelectedComponentSettings = newComp;
    }

    private ObservableCollection<ComponentSettings> GetSelectedComponentSource(ComponentSettings selected)
    {
        return ComponentsService.CurrentComponents.Lines
            .FirstOrDefault(x => x.Children.Contains(selected))?
            .Children ?? ViewModel.SelectedComponentContainerChildren;
    }

    [RelayCommand]
    private void DuplicateComponent(ComponentSettings settings)
    {
        var list = GetSelectedComponentSource(settings);
        var index = list.IndexOf(settings);
        if (index == -1)
        {
            return;
        }
        index = Math.Min(list.Count - 1, index);

        var newSettings = ConfigureFileHelper.CopyObject(settings);
        list.Insert(index, newSettings);
        if (settings == ViewModel.SelectedComponentSettingsMain)
        {
            ViewModel.SelectedComponentSettingsMain = newSettings;
        }
        else
        {
            ViewModel.SelectedComponentSettingsChild = newSettings;
        }
        ViewModel.SelectedComponentSettings = newSettings;
    }

    private void MenuItemDuplicateComponent_OnClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedComponentSettings != null)
        {
            DuplicateComponent(ViewModel.SelectedComponentSettings);
        }
    }

    [RelayCommand]
    private void MoveComponentToPreviousLine(ComponentSettings settings)
    {
        var list = GetSelectedComponentSource(settings);
        if (list == ViewModel.SelectedComponentContainerChildren)
        {
            return;
        }

        var index = ComponentsService.CurrentComponents.Lines.IndexOf(
            ComponentsService.CurrentComponents.Lines.First(x => x.Children == list));

        list.Remove(settings);
        if (index - 1 >= 0)
        {
            ComponentsService.CurrentComponents.Lines[index - 1].Children.Add(settings);
            return;
        }

        var newLine = new MainWindowLineSettings { Children = { settings } };
        ComponentsService.CurrentComponents.Lines.Insert(0, newLine);
        CommonDialog.ShowHint("已向上创建新主界面行。");
    }

    [RelayCommand]
    private void MoveComponentToNextLine(ComponentSettings settings)
    {
        var list = GetSelectedComponentSource(settings);
        if (list == ViewModel.SelectedComponentContainerChildren)
        {
            return;
        }

        var index = ComponentsService.CurrentComponents.Lines.IndexOf(
            ComponentsService.CurrentComponents.Lines.First(x => x.Children == list));

        list.Remove(settings);
        if (index + 1 < ComponentsService.CurrentComponents.Lines.Count)
        {
            ComponentsService.CurrentComponents.Lines[index + 1].Children.Add(settings);
            return;
        }

        var newLine = new MainWindowLineSettings { Children = { settings } };
        ComponentsService.CurrentComponents.Lines.Add(newLine);
        CommonDialog.ShowHint("已向下创建新主界面行。");
    }

    [RelayCommand]
    private void MoveToCurrentContainerComponent(ComponentSettings settings)
    {
        var list = GetSelectedComponentSource(settings);
        if (list == ViewModel.SelectedComponentContainerChildren)
        {
            return;
        }

        if (settings == ViewModel.SelectedRootComponent)
        {
            CommonDialog.ShowError("不能将容器组件移动到自身（或其子级）的子组件中。");
            return;
        }

        list.Remove(settings);
        ViewModel.SelectedComponentContainerChildren.Add(settings);
        Dispatcher.InvokeAsync(() => ViewModel.SelectedComponentSettingsChild = settings);
    }

    [RelayCommand]
    private void MoveComponentsToMainLines(ComponentSettings settings)
    {
        if (!ViewModel.SelectedComponentContainerChildren.Remove(settings))
        {
            return;
        }

        var selectedList = ViewModel.SelectedMainWindowLineSettings?.Children ??
                           ComponentsService.CurrentComponents.Lines.FirstOrDefault()?.Children;

        selectedList?.Add(settings);
    }

    public void DragEnter(IDropInfo dropInfo)
    {
    }

    public void DragLeave(IDropInfo dropInfo)
    {
    }

    public new void DragOver(IDropInfo dropInfo)
    {
        // TODO: 如果拖入的组件是当前组件的父组件，要拒绝拖入到子容器中。
        if (dropInfo.Data is not ComponentInfo && dropInfo.Data is not ComponentSettings)
            return;
        if (dropInfo.Data is ComponentSettings settings && settings == ViewModel.SelectedRootComponent 
            && Equals(dropInfo.TargetCollection, ViewModel.SelectedComponentContainerChildren))
            return;
        dropInfo.DropTargetAdorner = DropTargetAdorners.Insert;
        dropInfo.Effects = dropInfo.Data switch
        {
            ComponentInfo => DragDropEffects.Copy,
            ComponentSettings => DragDropEffects.Move,
            _ => DragDropEffects.None
        };
    }

    public new void Drop(IDropInfo dropInfo)
    {
        if (dropInfo.TargetCollection is not ObservableCollection<ComponentSettings> components)
        {
            return;
        }
        switch (dropInfo.Data)
        {
            case ComponentInfo info:
                var componentSettings = new ComponentSettings()
                {
                    Id = info.Guid.ToString()
                };
                components.Insert(dropInfo.InsertIndex, componentSettings);
                ClassIsland.Services.ComponentsService.LoadComponentSettings(componentSettings,
                    componentSettings.AssociatedComponentInfo.ComponentType!.BaseType!);
                break;
            case ComponentSettings settings:
                var oldIndex = components.IndexOf(settings);
                var newIndex = oldIndex < dropInfo.UnfilteredInsertIndex ? dropInfo.UnfilteredInsertIndex - 1 : dropInfo.UnfilteredInsertIndex;
                var finalIndex = newIndex >= components.Count ? components.Count - 1 : newIndex;
                if (!components.Contains(settings))
                {
                    var source = dropInfo.DragInfo.SourceCollection as ObservableCollection<ComponentSettings>;
                    source?.Remove(settings);
                    components.Insert(dropInfo.UnfilteredInsertIndex, settings);
                    break;
                }
                if (oldIndex != finalIndex)
                {
                    components.Move(oldIndex, finalIndex);
                }
                break;
        }
    }
}
