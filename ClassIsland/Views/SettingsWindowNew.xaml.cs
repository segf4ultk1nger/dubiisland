using ClassIsland.Core.Controls;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Navigation;
using System.Windows.Threading;
using ClassIsland.Core;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Abstractions.Services.Management;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Enums.SettingsWindow;
using ClassIsland.Core.Services;
using ClassIsland.Core.Services.Registry;
using ClassIsland.Shared;
using ClassIsland.ViewModels;
using MahApps.Metro.Controls;
using MahApps.Metro.Controls.Dialogs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ClassIsland.Services;
using CommonDialog = ClassIsland.Core.Controls.CommonDialog.CommonDialog;
using System.IO;
using ClassIsland.Controls;
using Path = System.IO.Path;
using System.Web;
using ClassIsland.Core.Enums;
using ClassIsland.Core.Models.SettingsWindow;
using Application = System.Windows.Application;
using ClassIsland.Helpers;
using ClassIsland.Shared.Helpers;
using IccEvolved.UiAccess;
using System.Transactions;

namespace ClassIsland.Views;

/// <summary>
/// SettingsWindowNew.xaml 的交互逻辑
/// </summary>
public partial class SettingsWindowNew : MyWindow
{
    private const string KeepHistoryParameterName = "ci_keepHistory";

    public SettingsNewViewModel ViewModel { get; } = new();

    /// <summary>
    /// 汉堡菜单项（设置页面 + 分组分隔线）
    /// </summary>
    public ObservableCollection<object> MenuItems { get; } = new();

    [NotNull]
    public NavigationService? NavigationService { get; set; }

    private bool IsOpened { get; set; } = false;

    public IManagementService ManagementService { get; }

    private IHangService HangService { get; }

    private ILogger<SettingsWindowNew> Logger;

    private DiagnosticService DiagnosticService { get; }

    public SettingsService SettingsService { get; }

    public static readonly string StartupSettingsPage = "general";

    private IComponentsService ComponentsService { get; }

    private string LaunchSettingsPage { get; set; } = StartupSettingsPage;

    private readonly Dictionary<string, SettingsPageBase?> _cachedPages = new();

    /// <summary>当前正在导航到的页面，用于在快速点击时补上被 <see cref="ViewModel"/> 导航锁丢弃的目标页。</summary>
    private SettingsPageInfo? _navigatingPageInfo;

    /// <summary>搜索结果点击后，导航完成时要滚动到并高亮的卡片。</summary>
    private SettingsSearchEntry? _pendingSearchTarget;

    private readonly DispatcherTimer SearchDebounceTimer = new() { Interval = TimeSpan.FromMilliseconds(120) };


    public SettingsWindowNew(IManagementService managementService, IHangService hangService,
        ILogger<SettingsWindowNew> logger, DiagnosticService diagnosticService, SettingsService settingsService,
        IComponentsService componentsService, IUriNavigationService uriNavigationService)
    {
        Logger = logger;
        DataContext = this;
        ManagementService = managementService;
        ComponentsService = componentsService;
        DiagnosticService = diagnosticService;
        HangService = hangService;
        SettingsService = settingsService;
        SettingsService.Settings.PropertyChanged += SettingsOnPropertyChanged;
        Closed += SettingsWindowNew_OnClosed;
        InitializeComponent();
        AnimateSearchOverlay(ViewModel.IsNavigationDrawerOpened);
        NavigationService = NavigationFrame.NavigationService;
        NavigationService.LoadCompleted += NavigationServiceOnLoadCompleted;
        NavigationService.Navigating += NavigationServiceOnNavigating;
        ViewModel.PropertyChanged += ViewModelOnPropertyChanged;
        RebuildMenu();
        SearchDebounceTimer.Tick += (_, _) =>
        {
            SearchDebounceTimer.Stop();
            RunSearch(SearchTextBox.Text);
        };

        if (ManagementService.Policy.DisableSettingsEditing)
        {
            LaunchSettingsPage = "about";
        }
    }

    private async void ViewModelOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModel.SelectedPageInfo))
        {
            if (!IsLoaded || !ViewModel.IsRendered)
                return;
            await CoreNavigate(ViewModel.SelectedPageInfo);
        }
        else if (e.PropertyName == nameof(ViewModel.IsNavigationDrawerOpened))
        {
            AnimateSearchOverlay(ViewModel.IsNavigationDrawerOpened);
        }
    }

    /// <summary>
    /// 紧凑覆盖（CompactOverlay）模式下，导航栏是滑出并靠内部 Clip 逐渐露出的，而搜索框是独立的覆盖层，
    /// 不会跟着被裁剪。这里让搜索框宽度从紧凑宽度动画到展开宽度（配合 ClipToBounds 露出）并同步淡入淡出，
    /// 缓动曲线与时长都取自 MahApps SplitView 的展开/收起 Storyboard（KeySpline 0.1,0.9 0.2,1.0）。
    /// </summary>
    private void AnimateSearchOverlay(bool open)
    {
        var compactLength = HamburgerMenuControl.CompactPaneLength;
        var openLength = HamburgerMenuControl.OpenPaneLength;

        // 宽屏内联展开、或窗口尚未加载（初始布局）时不做动画，直接落到目标状态。
        if (!ViewModel.IsViewCompressed || !IsLoaded)
        {
            SearchOverlay.BeginAnimation(FrameworkElement.WidthProperty, null);
            SearchOverlay.BeginAnimation(OpacityProperty, null);
            SearchOverlay.Opacity = open ? 1 : 0;
            SearchOverlay.Width = open ? openLength : compactLength;
            SearchOverlay.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            return;
        }

        if (open)
        {
            SearchOverlay.Visibility = Visibility.Visible;
            SearchOverlay.BeginAnimation(OpacityProperty, null);
            SearchOverlay.Opacity = 1;
            SearchOverlay.BeginAnimation(FrameworkElement.WidthProperty,
                CreateOverlayTransition(compactLength, openLength, TimeSpan.FromMilliseconds(350)));
            return;
        }

        var widthAnimation = CreateOverlayTransition(openLength, compactLength, TimeSpan.FromMilliseconds(120));
        widthAnimation.Completed += (_, _) =>
        {
            if (!ViewModel.IsNavigationDrawerOpened)
            {
                SearchOverlay.BeginAnimation(FrameworkElement.WidthProperty, null);
                SearchOverlay.Opacity = 0;
                SearchOverlay.Width = compactLength;
                SearchOverlay.Visibility = Visibility.Collapsed;
            }
        };
        SearchOverlay.BeginAnimation(FrameworkElement.WidthProperty, widthAnimation);
        SearchOverlay.BeginAnimation(OpacityProperty,
            CreateOverlayTransition(1, 0, TimeSpan.FromMilliseconds(120)));
    }

    /// <summary>复刻 MahApps SplitView 的缓动：KeySpline 0.1,0.9 0.2,1.0（WPF 没有 SplineEase，需用关键帧）。</summary>
    private static DoubleAnimationUsingKeyFrames CreateOverlayTransition(double from, double to, TimeSpan duration)
    {
        var animation = new DoubleAnimationUsingKeyFrames();
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(from, KeyTime.FromPercent(0)));
        animation.KeyFrames.Add(new SplineDoubleKeyFrame(to, KeyTime.FromTimeSpan(duration),
            new KeySpline(0.1, 0.9, 0.2, 1.0)));
        return animation;
    }

    private void SettingsOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsService.Settings.IsDebugOptionsEnabled))
        {
            RebuildMenu();
        }
    }

    /// <summary>
    /// 重建汉堡菜单项。按分类分组，组间插入分隔线。
    /// </summary>
    private void RebuildMenu()
    {
        MenuItems.Clear();
        var firstGroup = true;
        foreach (var category in new[]
                 {
                     SettingsPageCategory.Internal,
                     SettingsPageCategory.External,
                     SettingsPageCategory.About,
                     SettingsPageCategory.Debug
                 })
        {
            var pages = SettingsWindowRegistryService.Registered
                .Where(p => p.Category == category && PassesNavigationFilter(p))
                .ToList();
            if (pages.Count == 0)
            {
                continue;
            }

            if (!firstGroup && category != SettingsPageCategory.External)
            {
                MenuItems.Add(new HamburgerMenuSeparatorItem());
            }

            foreach (var page in pages)
            {
                MenuItems.Add(page);
            }

            firstGroup = false;
        }
    }

    private bool PassesNavigationFilter(SettingsPageInfo item)
    {
        if (item.HideDefault)
        {
            return false;
        }
        if (item.Category is SettingsPageCategory.Internal or SettingsPageCategory.External && ManagementService.Policy.DisableSettingsEditing)
        {
            return false;
        }
        if (item.Category == SettingsPageCategory.Debug && ManagementService.Policy.DisableDebugMenu)
        {
            return false;
        }
        if (item.Category == SettingsPageCategory.Debug && !SettingsService.Settings.IsDebugOptionsEnabled)
        {
            return false;
        }

        return true;
    }

    protected override async void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        var page = SettingsWindowRegistryService.Registered.FirstOrDefault(x => x.Id == LaunchSettingsPage);
        ViewModel.IsRendered = true;
        await CoreNavigate(page);
        //await CoreNavigate(ViewModel.SelectedPageInfo);
    }

    private async void NavigationServiceOnNavigating(object sender, NavigatingCancelEventArgs e)
    {
        ViewModel.IsNavigating = true;
    }

    private async void NavigationServiceOnLoadCompleted(object sender, NavigationEventArgs e)
    {
        if (e.ExtraData is SettingsWindowNavigationData { IsNavigateFromSettingsWindow: true } data)
        {
            try
            {
                // 如果是从设置导航栏导航的，并且没有要求保留历史记录，那么就要清除掉返回项目
                if (!data.KeepHistory)
                {
                    NavigationService.RemoveBackEntry();
                }

                if (!IThemeService.IsWaitForTransientDisabled)
                {
                    await Dispatcher.Yield();
                }

                ViewModel.IsNavigating = false;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "无法完成设置页面导航 {}", ViewModel.SelectedPageInfo?.Id);
            }
        }
        ViewModel.IsNavigating = false;
        ViewModel.CanGoBack = NavigationService.CanGoBack;

        // 搜索命中：目标页加载完成后滚动到对应卡片并高亮。
        if (_pendingSearchTarget is { } searchTarget)
        {
            _pendingSearchTarget = null;
            if (ViewModel.SelectedPageInfo?.Id == searchTarget.PageId)
            {
                Dispatcher.BeginInvoke(new System.Action(() => RevealSearchTarget(searchTarget)),
                    DispatcherPriority.ContextIdle);
            }
        }

        // 快速连续点击导航时，后一次点击会被 IsNavigating 锁丢弃，导致菜单高亮与内容不一致。
        // 这里在当前导航完成后补上最后一次被丢弃的目标页。
        if (ViewModel.IsRendered && ViewModel.SelectedPageInfo is { } target &&
            !ReferenceEquals(target, _navigatingPageInfo))
        {
            await CoreNavigate(target);
        }
    }


    private async void NavigationListBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
    }

    private async Task CoreNavigate(SettingsPageInfo? info, Uri? uri = null)
    {
        Logger.LogTrace("pre-开始导航");
        if (info == null)
        {
            return;
        }

        switch (info.Category)
        {
            // 判断是否可以导航
            case SettingsPageCategory.Internal or SettingsPageCategory.External when
                ManagementService.Policy.DisableSettingsEditing:
            case SettingsPageCategory.Debug when
                ManagementService.Policy.DisableDebugMenu:
                return;
        }

        if (ViewModel.IsNavigating)
        {
            return;
        }
        Logger.LogTrace("开始导航");
        ViewModel.IsPopupOpen = false;
        ViewModel.IsNavigating = true;
        try
        {
            if (ViewModel.IsViewCompressed)
            {
                ViewModel.IsNavigationDrawerOpened = false;
            }

            ViewModel.SelectedPageInfo = info;
            _navigatingPageInfo = info;

            var uriQuery = HttpUtility.ParseQueryString(uri?.Query ?? "");
            var keepHistory = uriQuery[KeepHistoryParameterName] == "true";

            HangService.AssumeHang();
            // 从ioc容器获取页面
            var page = GetPage(info.Id, out var cached);
            // 清空抽屉
            ViewModel.IsDrawerOpen = false;
            ViewModel.DrawerContent = null;
            // 进行导航
            if (!keepHistory)
            {
                NavigationService.RemoveBackEntry();
            }
            NavigationService.Navigate(page, new SettingsWindowNavigationData(true, uri != null, uri, keepHistory));
            //ViewModel.FrameContent;
            if (!keepHistory)
            {
                NavigationService.RemoveBackEntry();
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "无法完成设置页面导航 {}", info.Id);
            ViewModel.IsNavigating = false;
        }
        //finally
        //{
        //    ViewModel.IsNavigating = false;
        //}
    }

    private SettingsPageBase? GetPage(string? id, out bool cached)
    {
        cached = false;
        if (_cachedPages.TryGetValue(id ?? "", out var page))
        {
            cached = true;
            return page;
        }
        var pageNew = IAppHost.Host?.Services.GetKeyedService<SettingsPageBase>(id);
        if (pageNew != null)
        {
            _cachedPages[id ?? ""] = pageNew;
        }

        return pageNew;
    }

    private void SearchTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        SetSearchBoxExpanded(!string.IsNullOrWhiteSpace(SearchTextBox.Text));
        SearchDebounceTimer.Stop();
        SearchDebounceTimer.Start();
    }

    private void SetSearchBoxExpanded(bool expanded)
    {
        var animation = new ThicknessAnimation(
            expanded ? new Thickness(8, 9, 8, 9) : new Thickness(48, 9, 8, 9),
            TimeSpan.FromMilliseconds(160))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        SearchTextBox.BeginAnimation(FrameworkElement.MarginProperty, animation);
    }

    private void SearchTextBox_OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                if (SearchResultsList.Items.Count > 0)
                {
                    var entry = SearchResultsList.SelectedItem as SettingsSearchEntry
                                ?? SearchResultsList.Items[0] as SettingsSearchEntry;
                    if (entry != null)
                    {
                        ActivateSearchEntry(entry);
                    }
                }

                e.Handled = true;
                break;
            case Key.Down:
                if (SearchResultsList.Items.Count > 0)
                {
                    SearchResultsList.Focus();
                    SearchResultsList.SelectedIndex = Math.Max(0, SearchResultsList.SelectedIndex);
                }

                e.Handled = true;
                break;
            case Key.Escape:
                ClearSearch();
                e.Handled = true;
                break;
        }
    }

    private void SearchResultsList_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source &&
            ItemsControl.ContainerFromElement(SearchResultsList, source) is ListBoxItem { DataContext: SettingsSearchEntry entry })
        {
            ActivateSearchEntry(entry);
        }
    }

    private void RunSearch(string query)
    {
        EnsureIndexBuilt();

        query = (query ?? "").Trim();
        if (query.Length == 0)
        {
            SearchResultsPanel.Visibility = Visibility.Collapsed;
            SearchStatusPanel.Visibility = Visibility.Collapsed;
            SearchResultsList.ItemsSource = null;
            return;
        }

        if (!SettingsSearchIndex.IsBuilt)
        {
            SearchResultsPanel.Visibility = Visibility.Collapsed;
            SearchResultsList.ItemsSource = null;
            SearchStatusText.Text = "正在建立设置索引…";
            SearchStatusPanel.Visibility = Visibility.Visible;
            return;
        }

        var results = SettingsSearchIndex.Search(query);
        if (results.Count == 0)
        {
            SearchResultsPanel.Visibility = Visibility.Collapsed;
            SearchResultsList.ItemsSource = null;
            SearchStatusText.Text = "无结果";
            SearchStatusPanel.Visibility = Visibility.Visible;
        }
        else
        {
            SearchResultsList.ItemsSource = results;
            SearchStatusPanel.Visibility = Visibility.Collapsed;
            SearchResultsPanel.Visibility = Visibility.Visible;
        }
    }

    private void EnsureIndexBuilt()
    {
        if (!SettingsSearchIndex.IsBuilt)
        {
            SettingsSearchIndex.Build(PassesNavigationFilter);
        }
    }

    private async void ActivateSearchEntry(SettingsSearchEntry entry)
    {
        var info = SettingsWindowRegistryService.Registered.FirstOrDefault(x => x.Id == entry.PageId);
        if (info == null || !PassesNavigationFilter(info))
        {
            ClearSearch();
            return;
        }

        ClearSearch();

        // 紧凑覆盖模式下选中结果后收起导航栏；同页时不会走 CoreNavigate，得在这里收。
        if (ViewModel.IsViewCompressed)
        {
            ViewModel.IsNavigationDrawerOpened = false;
        }

        // 已经在目标页时，再 Navigate 不会触发 LoadCompleted，高亮得直接揭示。
        if (ViewModel.SelectedPageInfo?.Id == info.Id)
        {
            if (!entry.IsPage)
            {
                Dispatcher.BeginInvoke(new System.Action(() => RevealSearchTarget(entry)), DispatcherPriority.ContextIdle);
            }

            return;
        }

        _pendingSearchTarget = entry.IsPage ? null : entry;
        await CoreNavigate(info);
    }

    private void ClearSearch()
    {
        SearchTextBox.Text = "";
        SearchResultsPanel.Visibility = Visibility.Collapsed;
        SearchStatusPanel.Visibility = Visibility.Collapsed;
        SearchResultsList.ItemsSource = null;
    }

    private void RevealSearchTarget(SettingsSearchEntry entry)
    {
        var card = FindSettingsCard(NavigationFrame, entry.Header, entry.Occurrence);
        if (card == null)
        {
            return;
        }

        card.BringIntoView();
        SearchHighlightAdorner.Flash(card);
    }

    private static SettingsCard? FindSettingsCard(DependencyObject root, string header, int occurrence)
    {
        var seen = 0;
        foreach (var card in EnumerateVisual<SettingsCard>(root))
        {
            if (card.Header != header)
            {
                continue;
            }

            if (seen == occurrence)
            {
                return card;
            }

            seen++;
        }

        return null;
    }

    private static IEnumerable<T> EnumerateVisual<T>(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in EnumerateVisual<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private void SettingsWindowNew_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var wasCompressed = ViewModel.IsViewCompressed;
        ViewModel.IsViewCompressed = Width < 800 && WindowState != WindowState.Maximized;
        if (ViewModel.IsViewCompressed == wasCompressed)
        {
            return;
        }

        // 宽屏：CompactInline，菜单展开；窄屏：Overlay，菜单收起。
        ViewModel.IsNavigationDrawerOpened = !ViewModel.IsViewCompressed;
    }

    private void ButtonBaseToggleNavigationDrawer_OnClick(object sender, RoutedEventArgs e)
    {
        ViewModel.IsNavigationDrawerOpened = !ViewModel.IsNavigationDrawerOpened;
    }

    private void ButtonGoBack_OnClick(object sender, RoutedEventArgs e)
    {
        NavigationService.GoBack();
    }

    public async void Open()
    {
        if (!IsOpened)
        {
            if (!await ManagementService.AuthorizeByLevel(ManagementService.CredentialConfig.EditSettingsAuthorizeLevel))
            {
                return;
            }
            IsOpened = true;
            Show();
        }
        else
        {
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }

            Activate();
        }
    }

    public async void Open(string key, Uri? uri = null)
    {
        var page = SettingsWindowRegistryService.Registered.FirstOrDefault(x => x.Id == key) ?? ViewModel.SelectedPageInfo;
        LaunchSettingsPage = key;
        await CoreNavigate(page, uri);
        Open();
    }

    public void OpenUri(Uri uri)
    {
        if (uri.Segments.Length > 2)
        {
            var segment = uri.Segments[2];
            var uriSegment = segment.EndsWith("/") ? segment.Substring(0, segment.Length - 1) : segment;
            Open(uriSegment, uri);
        }
        else if (uri.Segments.Length == 2)
            Open();
    }

    private void SettingsWindowNew_OnClosing(object? sender, CancelEventArgs e)
    {
        e.Cancel = true;
        IsOpened = false;
        Hide();
        SettingsService.SaveSettings("关闭应用设置窗口");
        ComponentsService.SaveConfig();
        if (SettingsService.Settings.SettingsPagesCachePolicy <= 1)
        {
            _cachedPages.Clear();
        }
        GC.Collect();
    }

    private void SettingsWindowNew_OnClosed(object? sender, EventArgs e)
    {
        // 窗口被空闲销毁 → 退订单例设置事件、停掉计时器，避免窗口对象无法回收。
        SettingsService.Settings.PropertyChanged -= SettingsOnPropertyChanged;
        SearchDebounceTimer.Stop();
    }

    private void CommandBindingOpenDrawer_OnExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        ViewModel.DrawerContent = e.Parameter;
        ViewModel.IsDrawerOpen = true;
    }

    private void CommandBindingCloseDrawer_OnExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        ViewModel.IsDrawerOpen = false;
    }

    private bool _isRestartDialogOpen;

    private void ButtonRestartApp_OnClick(object sender, RoutedEventArgs e)
    {
        ShowRestartDialog();
    }

    private async void ShowRestartDialog()
    {
        if (_isRestartDialogOpen)
            return;
        _isRestartDialogOpen = true;
        try
        {
            var r = await DialogService.ShowMessageAsync(SettingsPageBase.DialogHostIdentifier,
                "需要重启", "部分设置需要重启以应用。", MessageDialogStyle.AffirmativeAndNegative,
                new MetroDialogSettings
                {
                    AffirmativeButtonText = "立即重启",
                    NegativeButtonText = "稍后",
                    DefaultButtonFocus = MessageDialogResult.Negative
                });
            if (r == MessageDialogResult.Affirmative)
            {
                AppBase.Current.Restart();
            }
        }
        finally
        {
            _isRestartDialogOpen = false;
        }
    }

    private void CommandBindingRestartApp_OnExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        ViewModel.IsRequestedRestart = true;
        ShowRestartDialog();
    }

    private void PopupButtonBase_OnClick(object sender, RoutedEventArgs e)
    {
        ViewModel.IsPopupOpen = false;
    }

    private void OpenDrawer(string key)
    {
        ViewModel.DrawerContent = FindResource(key);
        ViewModel.IsDrawerOpen = true;
    }

    private void MenuItemExperimentalSettings_OnClick(object sender, RoutedEventArgs e)
    {
        ViewModel.IsPopupOpen = false;
        OpenDrawer("ExperimentalSettings");
    }

    private async void MenuItemExitManagement_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await ManagementService.ExitManagementAsync();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "无法退出管理。");
            CommonDialog.ShowError($"无法退出管理：{ex.Message}");
        }
    }

    private void MenuItemAppLogs_OnClick(object sender, RoutedEventArgs e)
    {
        App.GetService<AppLogsWindow>().Open();
    }

    private async void MenuItemExportDiagnosticInfo_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var r = new CommonDialogBuilder()
                .SetContent("您正在导出应用的诊断数据。导出的诊断数据将包含应用 30 天内产生的日志、系统及环境信息、应用设置、当前加载的档案和集控设置（如有），可能包含敏感信息，请在导出后注意检查。")
                .SetIconKind(CommonDialogIconKind.Hint)
                .AddCancelAction()
                .AddAction("继续", IconGlyphs.Check, true)
                .ShowDialog();
            
            if (r != 1)
                return;
            var dialog = new SaveFileDialog()
            {
                Title = "导出诊断数据",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                Filter = "压缩文件(*.zip)|*.zip"
            };
            if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                return;
            await DiagnosticService.ExportDiagnosticData(dialog.FileName);
        }
        catch (Exception exception)
        {
            CommonDialog.ShowError($"导出失败：{exception.Message}");
        }
    }

    private void NavigationCollectionViewSource_OnFilter(object sender, FilterEventArgs e)
    {
        if (e.Item is not SettingsPageInfo item)
            return;
        if (item.HideDefault)
        {
            e.Accepted = false;
            return;
        }
        if (item.Category is SettingsPageCategory.Internal or SettingsPageCategory.External && ManagementService.Policy.DisableSettingsEditing)
        {
            e.Accepted = false;
            return;
        }
        if (item.Category == SettingsPageCategory.Debug && ManagementService.Policy.DisableDebugMenu)
        {
            e.Accepted = false;
            return;
        }

        if (item.Category == SettingsPageCategory.Debug && !SettingsService.Settings.IsDebugOptionsEnabled)
        {
            e.Accepted = false;
            return;
        }
    }

    private void MenuItemOpenLogFolder_OnClick(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo()
        {
            FileName = Path.GetFullPath(App.AppLogFolderPath) ?? "",
            UseShellExecute = true
        });
    }

    private void MenuItemOpenAppFolder_OnClick(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo()
        {
            FileName = Path.GetFullPath(".") ?? "",
            UseShellExecute = true
        });
    }

    private void MenuItemDebugWindowRule_OnClick(object sender, RoutedEventArgs e)
    {
        IAppHost.GetService<WindowRuleDebugWindow>().Show();
    }

    private void MenuItemOpenDataFolder_OnClick(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo()
        {
            FileName = Path.GetFullPath(App.AppRootFolderPath) ?? "",
            UseShellExecute = true
        });
    }

    private async void MenuItemAddClassSwapShortcut_OnClick(object sender, RoutedEventArgs e)
    {
        if (!SettingsService.Settings.IsUrlProtocolRegistered)
        {
            var urlDialogResult = new CommonDialogBuilder()
                .SetContent("快捷换课快捷方式需要启用【注册 Url 协议】选项才能工作。您要启用它吗？")
                .AddCancelAction()
                .AddAction("启用", IconGlyphs.Check, true)
                .SetIconKind(CommonDialogIconKind.Hint)
                .ShowDialog(this);
            if (urlDialogResult == 0)
            {
                return;
            }

            SettingsService.Settings.IsUrlProtocolRegistered = true;
        }
        var dialog = new SaveFileDialog()
        {
            Filter = "快捷方式（*.url）|*.url",
            FileName = "快捷换课.url",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
        };

        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
        {
            return;
        }

        await ShortcutHelpers.CreateClassSwapShortcutAsync(dialog.FileName);

        ViewModel.StatusMessage = "快捷换课图标创建成功。";
    }

    private async void MenuItemRestartToRecovery_OnClick(object sender, RoutedEventArgs e)
    {
        if (!await ManagementService.AuthorizeByLevel(ManagementService.CredentialConfig.ExitApplicationAuthorizeLevel))
        {
            return;
        }
        AppBase.Current.Restart(["-m", "-r"]);
    }

    private async void MenuItemRestartAsAdmin_OnClick(object sender, RoutedEventArgs e)
    {
        ViewModel.IsPopupOpen = false;
        if (!await ManagementService.AuthorizeByLevel(ManagementService.CredentialConfig.ExitApplicationAuthorizeLevel))
        {
            return;
        }
        if (UiAccessLauncher.IsElevated())
        {
            CommonDialog.ShowInfo("当前已经以管理员身份运行。");
            return;
        }

        var exe = FrameworkCompat.ProcessPath.Replace(".dll", ".exe");
        var startInfo = new ProcessStartInfo(exe)
        {
            UseShellExecute = true,
            Verb = "runas",
            Arguments = "-m"
        };
        try
        {
            Process.Start(startInfo);
        }
        catch (Win32Exception)
        {
            return;
        }

        AppBase.Current.Stop();
    }

    private void ButtonDoNotClickVeryDangerous_OnClick(object sender, RoutedEventArgs e)
    {
        var result = new CommonDialogBuilder()
            .SetIconKind(CommonDialogIconKind.Hint)
            .SetContent("警告！LegacyIsland 开发者不对应用接下来的行为造成的任何后果负责，并且不接受有关这些行为的任何 Bug 反馈。您确定要继续吗？")
            .AddAction("OK", IconGlyphs.HandOkay)
            .AddAction("搞定", IconGlyphs.ThumbUpOutline)
            .AddAction("继续", IconGlyphs.ArrowRight)
            .AddAction("确定", IconGlyphs.Check, true)
            .ShowDialog();

        var random = new Random();
        var value = random.Next(0, 65535) % 4;
        Console.WriteLine(value);
        switch (value)
        {
            case 0:
                BeginScaleEffect(0.25);
                break;
            case 1:
                BeginScaleEffect(2);
                break;
            case 2:
                BeginRotateEffect();
                break;
            case 3:
                BeginFlipEffect();
                break;
        }
        
    }

    private void BeginScaleEffect(double scale)
    {
        MinWidth = 0;
        var sb = new Storyboard();
        var daX = new DoubleAnimation(1, scale, TimeSpan.FromSeconds(1))
        {
            EasingFunction = new CircleEase()
        };
        Storyboard.SetTarget(daX, RootGrid);
        Storyboard.SetTargetProperty(daX, new PropertyPath("(0).(1)[0].(2)", [LayoutTransformProperty, TransformGroup.ChildrenProperty, ScaleTransform.ScaleXProperty]));
        var daY = new DoubleAnimation(1, scale, TimeSpan.FromSeconds(1))
        {
            EasingFunction = new CircleEase()
        };
        Storyboard.SetTarget(daY, RootGrid);
        Storyboard.SetTargetProperty(daY, new PropertyPath("(0).(1)[0].(2)", [LayoutTransformProperty, TransformGroup.ChildrenProperty, ScaleTransform.ScaleYProperty]));

        var wX = new DoubleAnimation(ActualWidth, ActualWidth * scale, TimeSpan.FromSeconds(1))
        {
            EasingFunction = new CircleEase()
        };
        Storyboard.SetTarget(wX, this);
        Storyboard.SetTargetProperty(wX, new PropertyPath(WidthProperty));
        var wY = new DoubleAnimation(ActualHeight, ActualHeight * scale, TimeSpan.FromSeconds(1))
        {
            EasingFunction = new CircleEase()
        };
        Storyboard.SetTarget(wY, this);
        Storyboard.SetTargetProperty(wY, new PropertyPath(HeightProperty));
        sb.Children.Add(daX);
        sb.Children.Add(daY);
        sb.Children.Add(wX);
        sb.Children.Add(wY);
        sb.Begin(this);
    }

    private void BeginRotateEffect()
    {
        var sb = new Storyboard();
        var daX = new DoubleAnimation(0, SharedRandom.Next(0, 3600) / 10.0, TimeSpan.FromSeconds(1))
        {
            EasingFunction = new CircleEase()
        };
        Storyboard.SetTarget(daX, RootGrid);
        Storyboard.SetTargetProperty(daX, new PropertyPath("(0).(1)[2].(2)", [LayoutTransformProperty, TransformGroup.ChildrenProperty, RotateTransform.AngleProperty]));
        
        sb.Children.Add(daX);
        sb.Begin(this);
    }

    private void BeginFlipEffect()
    {
        var sb = new Storyboard();
        var daX = new DoubleAnimation(1, -1, TimeSpan.FromSeconds(1))
        {
            EasingFunction = new CircleEase()
        };
        Storyboard.SetTarget(daX, RootGrid);
        Storyboard.SetTargetProperty(daX, new PropertyPath("(0).(1)[0].(2)", [LayoutTransformProperty, TransformGroup.ChildrenProperty, ScaleTransform.ScaleXProperty]));

        sb.Children.Add(daX);
        sb.Begin(this);
    }
}