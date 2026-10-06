# 组件设置页重设计 + 岛编辑器 —— 方案

> 供压缩上下文后继续实施。本文自包含，包含现状、决策、详细设计、关键 API 与落地顺序。

## 0. 背景与现状

- 仓库：`D:\vs\DubiIsland`，ClassIsland 1.x 的 WPF / net472 分支（LegacyIsland），UI 用 **MahApps.Metro**，图标用 **RemixIcon**（`MahApps.Metro.IconPacks` 的 `PackIconRemixIcon`）。禁止 Segoe MDL2 / `IconGlyphs`（历史遗留，作者反感）。
- **模型已迁移到 CI2 的 Lines 模型**（近期提交 `d180e741`）：
  - `IComponentsService.CurrentComponents` 现在是 `ComponentProfile`（`ClassIsland.Core/Abstractions/Services/IComponentsService.cs`）。
  - `ComponentProfile.Lines` → `ObservableCollection<MainWindowLineSettings>`（`ClassIsland.Core/Models/Components/ComponentProfile.cs`）。
  - `MainWindowLineSettings.Children` → `ObservableCollection<ComponentSettings>`，另有 `IsMainLine` / `IsNotificationEnabled` / `HideOnRule` / `HidingRules` 等（`ClassIsland.Core/Models/Components/MainWindowLineSettings.cs`）。
  - `ComponentSettings`（`ClassIsland.Core/Models/Components/ComponentSettings.cs`）：`Id` / `NameCache` / `Settings` / `Children`(=容器子组件，来自 `IComponentContainerSettings`) / `AssociatedComponentInfo` / `HideOnRule` / `HidingRules` / 一批布局外观属性（对齐、宽高、字体、颜色）。**`RelativeLineNumber` 已删除**。
- 当前设置页是「外层行列表 + 内层横向组件列表」的二维列表：`ClassIsland/Views/SettingPages/ComponentsSettingsPage.xaml(.cs)`、`ClassIsland/ViewModels/SettingsPages/ComponentsSettingsViewModel.cs`。**将被本方案替换**。
- **自绘岛（self-drawn island）是当前重点**：`Settings.UseSelfDrawnIsland` 为 true 时走 `ClassIsland/Controls/Island/IslandHost.cs`（裸 `HwndSource` 自绘）。XAML 主窗口路径（`MainWindow.xaml`）已损坏，作者明确**不修**。
- **重要事实**：自绘岛**不消费行级外观**（不透明度/字体/颜色/分体）。`IslandRenderer` 只用全局 `Settings` 的 `Opacity`/背景/字体/前景；行只作为「换行分组」。因此设置页**不要放行级外观控件**（会是假控件）。

## 1. 设计决策

设置页需要的是**效率**，不是「所见即所得」。因此拆成两个独立的 UI，共用同一份模型：

1. **设置页（Phase 1，先做）**：Figma 式 **左树 + 右属性面板**，竖直双栏；顶部加一条**只读岛预览**。
   - 树=结构化批量编辑；预览=确认「长啥样/顺序对不对」。两全其美，且比 CI2 的「两套 UI」省事。
2. **岛编辑器（Phase 2，后做）**：真正的 WYSIWYG，直接在主界面上拖组件。复用 `IslandRenderer` 已算好的几何做命中测试，工作量可控。

**非目标（本方案不做）**：
- 不做行级外观设置 UI（岛不消费）。
- 不修 XAML 主窗口。
- 不在设置页模拟二维岛的排布。

## 2. Phase 1：设置页（左树 + 右属性 + 顶部预览）

### 2.1 布局

```
┌──────────────────────────────────────────────┐
│ 配置方案 [Default ▾]  ⟳  ＋  📁                │   ← 顶部工具条（沿用现有）
├──────────────────────────────────────────────┤
│   [ 只读岛预览（实时跟随） ]                    │   ← IslandSurface，IsHitTestVisible=False
├───────────────┬──────────────────────────────┤
│  组件树        │  属性面板（滚动）              │
│  (280px)      │                              │
│  主界面        │  <选中项对应内容>             │
│   ├ 行 1 ★ ⏰ │                              │
│   │  ├ 时钟    │                              │
│   │  └ 课程表  │                              │
│   └ 行 2       │                              │
│      └ 容器 ▸  │                              │
│         ├ 日期 │                              │
│         └ 天气 │                              │
├───────────────┴──────────────────────────────┤
```

- 根 Grid：`Row0=Auto`（工具条）、`Row1=Auto`（预览）、`Row2=*`（双栏）。
- 双栏：`Column0=280`（树）、`Column1=Auto`（`GridSplitter`，2px）、`Column2=*`（属性）。
- 保留现有的：配置方案 ComboBox、刷新/新建/打开文件夹按钮（见当前 `ComponentsSettingsPage.xaml:ROW1`）。

### 2.2 树的数据与模板

单一数据源：`ComponentsService.CurrentComponents.Lines`。**直接绑定模型**，不额外造节点 VM（省事；选中项是 `object`，用 `is` 判别）。

```xml
<TreeView x:Name="TreeComponents"
          ItemsSource="{Binding ComponentsService.CurrentComponents.Lines}"
          SelectedItemChanged="TreeComponents_OnSelectedItemChanged"
          dd:DragDrop.IsDragSource="True"
          dd:DragDrop.IsDropTarget="True"
          dd:DragDrop.DropHandler="{Binding RelativeSource={RelativeSource AncestorType=local:ComponentsSettingsPage}}">
  <TreeView.Resources>
    <!-- 行节点 -->
    <HierarchicalDataTemplate DataType="{x:Type components:MainWindowLineSettings}"
                              ItemsSource="{Binding Children}">
      <!-- 行名 + IsMainLine/IsNotificationEnabled 徽标开关(见 2.5) -->
    </HierarchicalDataTemplate>
    <!-- 组件节点（容器会展开 Children） -->
    <HierarchicalDataTemplate DataType="{x:Type components:ComponentSettings}"
                              ItemsSource="{Binding Children}">
      <!-- 图标 + 名称，容器显示展开箭头 -->
    </HierarchicalDataTemplate>
  </TreeView.Resources>
</TreeView>
```

- 行节点标题用序号（用 `ItemsControl`/转换器取父集合索引，或 VM 里维护一个 `ObservableCollection<ComponentTreeLineNode>` 以带序号——**若取序号麻烦，直接显示「行」**，序号非必需）。
- 组件图标沿用当前做法：`controls2:IconText Kind="{Binding AssociatedComponentInfo.PackIcon}"`（`ComponentInfo.PackIcon` 是组件自带字形，非页面 chrome，保留）。页面 chrome 图标用 `remix:PackIconRemixIcon`（元素形式，**不能写成 `{remix:PackIconRemixIcon ...}`**，它不是 MarkupExtension）。
- 只读属性 `ComponentSettings.Children` 对非容器为 `null`，`ItemsSource="{Binding Children}"` 对 null 自然无子项，OK。

### 2.3 属性面板

右侧一个 `ContentControl`，`Content="{Binding ViewModel.SelectedNode}"`，用 `DataTemplateSelector` 或按类型切换模板：

- **选中组件** → 一个滚动列：
  1. 组件自带设置：`<controls2:ComponentPresenter Settings="{Binding}" IsPresentingSettings="True"/>`（`SettingsType==null` 时隐藏）。
  2. 之后接现有的外观/高级卡片（**从当前 `ComponentsSettingsPage.xaml` 的「高级设置」TabItem 内容整段搬过来**）：按规则隐藏、字体大小、字体颜色、对齐方式、固定宽度、宽度限制、GUID。
  - **合并成一列滚动，不要再用 Tab。**
- **选中行** → 极简行面板：主要行、提醒、按规则隐藏（+ 编辑规则集按钮）。**不放不透明度/字体**。
- 未选中 → 空白或提示。

组件设置按钮沿用现有：`ButtonOpenRuleset_OnClick`（`OpenDrawer("RulesetControl")`）、`RulesetControl`（`ci:RulesetControl`）、`BindingProxy`。

### 2.4 只读岛预览（Phase 1 的核心新件）

**复用现成的渲染器**，不要重写绘制：

- 关键类型（都在 `ClassIsland/Controls/Island/`）：
  - `IslandContext`（public sealed）：ctor `(Settings, ILessonsService, IProfileService, IExactTimeService, IRulesetService, IWeatherService)`；另有可变属性 `PixelsPerDip`、`AccentColor`、`ForegroundColor`、`ThemeBackground`。
  - `IslandRenderer`（public sealed）：`IslandContext` 构造；`Add(IIslandComponent)`、`Clear()`、`Measure(Size)`、`Render(DrawingContext, Rect)`、`RefreshStyles()`、`Invalidated` 事件、`Scale`。
  - `IslandSurface`（**public sealed** `FrameworkElement`）：拥有 `DrawingVisual`，`MeasureOverride/ArrangeOverride` 自动调用 `Measure/Render`；属性 `WindowOvershootScale`、`HorizontalAlign`、`VerticalAlign`；`Redraw()`。
  - `IslandComponentFactory.Create(ComponentSettings, IslandContext)` → `IIslandComponent?`（public static）。
- 构建预览（对照 `IslandHost.BuildComponents` / `HookComponentCollections`，`ClassIsland/Controls/Island/IslandHost.cs:321` 起）：
  ```csharp
  // 页面构造注入：IComponentsService, SettingsService,
  //   ILessonsService, IProfileService, IExactTimeService, IRulesetService, IWeatherService, IThemeService
  var context = new IslandContext(settings, lessons, profile, exactTime, ruleset, weather)
  {
      PixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip,
      AccentColor = themeService.PrimaryColor,
      ForegroundColor = /* 见 IslandHost.RefreshTheme 的取色 */,
      ThemeBackground = /* 同上 */
  };
  var renderer = new IslandRenderer(context);
  var surface = new IslandSurface(renderer) { WindowOvershootScale = 1 };
  // 填充：
  renderer.Clear();
  var lines = componentsService.CurrentComponents.Lines;
  for (var i = 0; i < lines.Count; i++)
      foreach (var s in lines[i].Children)
      {
          var c = IslandComponentFactory.Create(s, context);
          if (c != null) { c.LineNumber = i; renderer.Add(c); }
      }
  renderer.RefreshStyles();
  ```
- 挂到界面：把 `surface` 放进预览区（例如 `<Border ...><ContentPresenter Content="{Binding ...}"/></Border>`，或代码里 `PreviewHost.Child = surface`）。预览区设 `IsHitTestVisible="False"`、`ClipToBounds="True"`，高度按内容（`Auto`，设个 `MaxHeight`）。
- **变更跟随**：与 `IslandHost` 相同——订阅 `CurrentComponents.Lines.CollectionChanged` 及每条 `Children.CollectionChanged`（行增删时重挂），以及 `SettingsService.Settings.PropertyChanged`（字体/颜色/缩放变化时重建或 `RefreshStyles`），重建组件树后 `renderer.Invalidate()`。
- 预览取色/字体的完整逻辑见 `IslandHost.RefreshTheme()`（`IslandHost.cs:936`）与 `ResolveThemeColor`。
- 建议封装成一个自包含控件 `ClassIsland/Controls/Island/IslandPreview.cs`（`UserControl`/`ContentControl`），内部持有 context/renderer/surface 并订阅刷新，设置页只放它。

> 备选（更省）：不加只读预览，仅做树+属性。但预览能补回「左右顺序」的空间直觉，强烈建议保留。

### 2.5 行内开关（主要行 / 提醒）

- 行节点右侧放两个 `mah:ToggleSwitch`（或小 `ToggleButton` + RemixIcon `StarFill` / `Notification3Fill`），绑 `IsMainLine` / `IsNotificationEnabled`。
- 主要行互斥：一个变 true 时其余置 false；全部取消时把第一行设回 true（参考当前 `ToggleButtonIsMainLine_OnClick`）。

### 2.6 命令与交互

复用/改写当前 `ComponentsSettingsPage.xaml.cs` 里已有的方法（它们在 `d180e741` 里已按 Lines 模型写好）：
- `ButtonCreateMainWindowLine_OnClick`（在当前选中行下方插行）、`ButtonRemoveSelectedMainWindowLine_OnClick`（≥1 行）、`ToggleButtonIsMainLine_OnClick`。
- `CreateContainerComponent(ComponentInfo)`（包裹到容器）、`DuplicateComponent`、`MoveToCurrentContainerComponent`、`MoveComponentsToMainLines`。
- 新增/替换：**添加组件**——小弹层/`ContextMenu` 列出 `ComponentRegistryService.Registered`（`ObservableCollection<ComponentInfo>`；**本仓库没有 `GetGroupedSortedComponents`/`ComponentGroup`，别用**）。选中后：
  ```csharp
  var cs = new ComponentSettings { Id = info.Guid.ToString() };
  ClassIsland.Services.ComponentsService.LoadComponentSettings(cs, cs.AssociatedComponentInfo.ComponentType!.BaseType!);
  targetList.Insert(index, cs);
  ```
  `LoadComponentSettings` 是 `ClassIsland.Services.ComponentsService` 的 `internal static`（同程序集可用）。
- 增删改 `Lines` / `Children` 会自动保存（`ComponentsService.HookConfigChanges` 已挂 `CollectionChanged`/`PropertyChanged`）。**不需要手动 SaveConfig。**

### 2.7 拖拽（GongSolutions）

- 已在用 `GongSolutions.Wpf.DragDrop`（`dd:` 命名空间），页面实现 `IDropTarget`。
- `TreeView` 设置 `dd:DragDrop.IsDragSource/IsDropTarget/DropHandler`，`DropHandler` 绑页面（`{Binding RelativeSource={RelativeSource AncestorType=local:ComponentsSettingsPage}}`）。
- 在 `DragOver/Drop` 里用 `dropInfo.TargetItem` 解析目标节点：
  - `TargetItem` 是 `MainWindowLineSettings` → 目标是**该行的 `Children`**。
  - `TargetItem` 是容器 `ComponentSettings` → 目标是**该容器的 `Children`**（`(Settings as IComponentContainerSettings)?.Children`）。
  - `TargetItem` 是普通 `ComponentSettings` → 目标是**它所在的列表**（在某行的 `Children` 里找到它）。
  - `dropInfo.TargetCollection` 有时直接可用，优先用它；否则按上面解析。
- 校验：容器不能拖进自己或自己的后代（递归查 `Children`）；`ComponentInfo`（新建）只能落列表。
- WPF `TreeView` 拖拽有毛边（放置指示器、自动展开、跨层级），预期要花时间调。参考 CI2 的 `EditableComponentsListBoxDropHandler.cs`（`C:\Users\dubi906w\Desktop\ClassIsland-master\ClassIsland\Controls\EditMode\`）取放置位置判定思路（左/右半 → 前/后插入）。

### 2.8 样式（Metro）

- 单一平面，**删掉嵌套 `GroupBox`/多层 `Border`**。
- 树：扁平项、accent 选中条、行内开关；chrome 图标 `remix:PackIconRemixIcon`（元素形式）。
- 属性：沿用 `controls2:SettingsCard` / `SettingsControl`（注意它们目前只有 `IconGlyph`(Segoe 字符串)；若要 Remix 图标需另加属性或用 `Content`，可后续再说）。
- 删除当前页面里为二维列表写的 `ComponentListBoxStyle`、多 ListBox 选中同步缓存（`MainWindowLineListBoxCacheReversed`）等——树只有单一选中，这些全删。

### 2.9 要改的文件

- 重写：`ClassIsland/Views/SettingPages/ComponentsSettingsPage.xaml`、`.xaml.cs`。
- 重写：`ClassIsland/ViewModels/SettingsPages/ComponentsSettingsViewModel.cs`（改为：`SelectedNode`、`SelectedLine`、`SelectedComponent`、`IsComponentSelected` 等）。
- 新增：`ClassIsland/Controls/Island/IslandPreview.cs`（预览控件）。
- 已删除：`ClassIsland/Views/SettingPages/ComponentsSettingsPageDropHandler.cs`（保持删除，页面自身作 `IDropTarget`）。

## 3. 关键 API / 文件速查（压缩上下文后用）

- 模型：`ClassIsland.Core/Models/Components/{ComponentProfile,MainWindowLineSettings,ComponentSettings}.cs`
- 服务接口：`ClassIsland.Core/Abstractions/Services/IComponentsService.cs`（`CurrentComponents` = `ComponentProfile`）
- 服务实现：`ClassIsland/Services/ComponentsService.cs`（`ComponentSettingsPath`、`DefaultComponentProfile`、`LoadComponentSettings`(internal static)、`HookConfigChanges`、自动保存）
- 组件注册：`ClassIsland.Core/Services/Registry/ComponentRegistryService.cs`（`Registered`、`RegisteredSettings`、`MigrationPairs`）
- 组件元数据：`ClassIsland.Core/Attributes/ComponentInfo.cs`（`Guid`/`Name`/`PackIcon`/`Description`/`SettingsType`/`ComponentType`/`IsComponentContainer`）
- 容器设置接口：`ClassIsland.Core/Abstractions/Models/IComponentContainerSettings.cs`（`Children`）
- 自绘渲染：`ClassIsland/Controls/Island/{IslandHost,IslandRenderer,IslandSurface,IslandContext,IslandComponentFactory,IIslandComponent,IslandComponentBase}.cs`
- 设置基类：`ClassIsland.Core/Abstractions/Controls/SettingsPageBase.cs`（`OpenDrawer`、`DialogHostIdentifier`）
- 通用控件：`ClassIsland.Core/Controls/{ComponentPresenter,SettingsCard,SettingsControl,NumbericTextBox,BindingProxy}.cs`
- 规则集：`ClassIsland.Core/Controls/Ruleset/RulesetControl`（XAML `ci:RulesetControl`）

## 4. Phase 2：WYSIWYG 岛编辑器（后做）

目标：在真实岛上直接拖组件（CI2 `EditModeView` 的等价物，但更简单——我们只有一条横带）。
要点：
- **复用几何**：`IslandRenderer.Measure` 已产出每行高度与每个 slot 的 `Rect`；命中测试/拖拽锚点直接用。渲染不用重写。
- 由于岛是**点击穿透的透明窗口**，编辑态需另开一个**可交互的正常窗口**（非 layered/非 transparent），在里面用同一 context/renderer 画岛 + 叠一层交互层。
- 侧边抽屉（WPF 里用滑入的 Grid/面板）装：组件库、选中组件属性、行设置、外观、配置方案。
- 容器子组件：就地点开子列表（CI2 用锚定在容器位置的浮动面板，`EditModeContainerComponentInfo`）。
- 入口：设置页/托盘放「在岛中编辑」按钮。
- 风险：命中测试、拖拽指示器、抽屉与拖动冲突（CI2 拖动时会临时收起抽屉）。单独排期。

## 5. 里程碑

1. **只读岛预览**（`IslandPreview`）嵌进设置页，验证 `IslandRenderer` 几何可复用、能实时跟随模型。← 先做，风险最小，能立刻看到效果。
2. **左树 + 右属性** 双栏，替换现有二维列表；命令用按钮/右键菜单（先不做拖拽）。
3. **树拖拽** + 跨行/进出容器 + 行内开关。
4. **Phase 2 WYSIWYG**。

## 6. 风险 / 开放问题

- WPF `TreeView` 拖拽是毛边最多的部分，预留时间；实在难用可先保证「按钮/右键菜单」路径完整，拖拽作为增强。
- 预览与真实岛的取色/缩放可能略有出入（预览在设置页背景上，岛在桌面透明背景上）。预览建议给一个中性底/描边，别指望像素级一致。
- 行节点序号需要父集合索引，WPF 绑定里不直接可得；若麻烦就显示「行」不加序号。
- `SettingsCard`/`SettingsControl` 目前是 Segoe `IconGlyph`；若要彻底 Remix 化需改这两个控件的图标接口（可独立小改动，别混进本方案主流程）。

## 7. 约定（来自 AGENTS.md）

- 图标一律 RemixIcon，禁 `IconGlyphs`/Segoe（组件自带图标 `ComponentInfo.PackIcon` 除外，那是组件元数据）。
- **不要自己 `dotnet build`**——作者用 Rider 构建运行；改完说明改了什么即可。
- 仅在被明确要求时提交；提交身份 `git -c user.name="douxiba" -c user.email="kriastans@protonmail.com"`；不提交 `.idea/`。
