using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Models.Components;
using ClassIsland.Shared;

namespace ClassIsland.Core.Controls;

/// <summary>
/// ComponentPresenter.xaml 的交互逻辑
/// </summary>
public partial class ComponentPresenter : UserControl, INotifyPropertyChanged
{
    public static readonly DependencyProperty SettingsProperty = DependencyProperty.Register(
        nameof(Settings), typeof(ComponentSettings), typeof(ComponentPresenter), new PropertyMetadata(default(ComponentSettings), PropertyChangedCallback));

    public static readonly DependencyProperty IsPresentingSettingsProperty = DependencyProperty.Register(
        nameof(IsPresentingSettings), typeof(bool), typeof(ComponentPresenter), new PropertyMetadata(false,
            (o, args) =>
            {
                // Settings 可能先于 IsPresentingSettings 生效，需在后者变化时用正确模式重渲染内容。
                if (o is ComponentPresenter presenter && presenter.Settings != null)
                {
                    presenter.UpdateContent(presenter.Settings);
                }
            }));

    public bool IsPresentingSettings
    {
        get { return (bool)GetValue(IsPresentingSettingsProperty); }
        set { SetValue(IsPresentingSettingsProperty, value); }
    }

    public static readonly DependencyProperty HideOnRuleProperty = DependencyProperty.Register(
        nameof(HideOnRule), typeof(bool), typeof(ComponentPresenter), new PropertyMetadata(false, (o, args) =>
        {
            if (o is ComponentPresenter control)
            {
                control.UpdateWindowRuleState();
            }
        }));

    public bool HideOnRule
    {
        get { return (bool)GetValue(HideOnRuleProperty); }
        set { SetValue(HideOnRuleProperty, value); }
    }

    public static readonly DependencyProperty HideConditionProperty = DependencyProperty.Register(
        nameof(HideCondition), typeof(string), typeof(ComponentPresenter), new PropertyMetadata("",
            (o, args) =>
            {
                if (o is ComponentPresenter control)
                {
                    control.CheckHideRule();
                }
            }));

    public string? HideCondition
    {
        get { return (string?)GetValue(HideConditionProperty); }
        set { SetValue(HideConditionProperty, value); }
    }

    public static readonly DependencyProperty IsOnMainWindowProperty = DependencyProperty.Register(
        nameof(IsOnMainWindow), typeof(bool), typeof(ComponentPresenter), new PropertyMetadata(default(bool),
            (o, args) =>
            {
                if (o is ComponentPresenter control)
                {
                    control.UpdateTheme();
                }
            }));

    public bool IsOnMainWindow
    {
        get { return (bool)GetValue(IsOnMainWindowProperty); }
        set { SetValue(IsOnMainWindowProperty, value); }
    }

    public static readonly DependencyProperty IsRootComponentProperty = DependencyProperty.Register(
        nameof(IsRootComponent), typeof(bool), typeof(ComponentPresenter), new PropertyMetadata(default(bool)));

    public bool IsRootComponent
    {
        get { return (bool)GetValue(IsRootComponentProperty); }
        set { SetValue(IsRootComponentProperty, value); }
    }

    private bool _isAllComponentsHid = false;

    public static readonly RoutedEvent ComponentVisibilityChangedEvent = EventManager.RegisterRoutedEvent(
        name: "ComponentVisibilityChanged",
        routingStrategy: RoutingStrategy.Bubble,
        handlerType: typeof(RoutedEventHandler),
        ownerType: typeof(ComponentPresenter));

    // Provide CLR accessors for adding and removing an event handler.
    public event RoutedEventHandler ComponentVisibilityChanged
    {
        add { AddHandler(ComponentVisibilityChangedEvent, value); }
        remove { RemoveHandler(ComponentVisibilityChangedEvent, value); }
    }

    private static void PropertyChangedCallback(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ComponentPresenter p)
        {
            p.UpdateContent(e.OldValue as ComponentSettings);
        }
    }

    private IConditionPulseService ConditionPulseService { get; } = IAppHost.GetService<IConditionPulseService>();

    private IScriptConditionEvaluator ConditionEvaluator { get; } = IAppHost.GetService<IScriptConditionEvaluator>();

    /// <summary>
    /// 组件设置控件缓存：设置控件首次测量时要把大量控件的模板套一遍（课程表这类会卡 100ms+），
    /// 复用已实例化的控件可以避免反复套模板。以 <see cref="ComponentSettings"/> 为键，删除组件后自动回收。
    /// </summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ComponentSettings, ComponentBase> SettingsControlCache = new();

    /// <summary>
    /// 预热：提前创建设置控件并在 <paramref name="host"/> 里测量一遍以套好模板，写入缓存，
    /// 使首次选中组件时不再阻塞。仅应在空闲（如 ApplicationIdle）时调用。
    /// </summary>
    public static void Prewarm(ComponentSettings settings, System.Windows.Controls.ContentControl host)
    {
        if (settings == null || SettingsControlCache.TryGetValue(settings, out _))
        {
            return;
        }

        var content = IAppHost.GetService<IComponentsService>().GetComponent(settings, true);
        if (content == null)
        {
            return;
        }

        try
        {
            host.Content = content;
            host.UpdateLayout();
            host.Content = null;
        }
        catch
        {
            // 预热失败无所谓，正式选中时会正常创建。
        }

        SettingsControlCache.Add(settings, content);
    }

    private object? _presentingContent;

    public ComponentSettings? Settings
    {
        get { return (ComponentSettings)GetValue(SettingsProperty); }
        set { SetValue(SettingsProperty, value); }
    }

    private bool _contentUpdateScheduled;

    private void UpdateContent(ComponentSettings? oldSettings)
    {
        if (oldSettings != null)
        {
            oldSettings.PropertyChanged -= SettingsOnPropertyChanged;
            if (oldSettings.Children != null)
            {
                oldSettings.Children.CollectionChanged -= ChildrenOnCollectionChanged;
            }
        }
        RaiseEvent(new RoutedEventArgs(ComponentVisibilityChangedEvent));
        if (Settings == null)
        {
            PresentingContent = null;
            return;
        }
        Settings.PropertyChanged += SettingsOnPropertyChanged;
        if (Settings.Children != null)
        {
            Settings.Children.CollectionChanged += ChildrenOnCollectionChanged;
        }

        // Settings 与 IsPresentingSettings 在 XAML 中分两次赋值：若先赋值 Settings，会先按「组件模式」
        // 实例化出真正的组件（课程表这类组件构建列表很慢），随后又被设置控件替换，白做一次昂贵实例化。
        // 这里合并到下一轮消息泵，只用最终模式创建一次。
        if (_contentUpdateScheduled)
        {
            return;
        }
        _contentUpdateScheduled = true;
        Dispatcher.BeginInvoke(new System.Action(() =>
        {
            _contentUpdateScheduled = false;
            ComponentBase? content;
            if (IsPresentingSettings && Settings != null &&
                SettingsControlCache.TryGetValue(Settings, out var cached))
            {
                content = cached;
            }
            else
            {
                content = IAppHost.GetService<IComponentsService>().GetComponent(Settings, IsPresentingSettings);
                if (IsPresentingSettings && Settings != null && content != null)
                {
                    SettingsControlCache.Add(Settings, content);
                }
            }
            // 理论上展示的内容的数据上下文应为MainWindow，这里不便用前端xaml绑定，故在后台设置。
            if (content != null && IsOnMainWindow && Window.GetWindow(this) is { } window)
            {
                content.DataContext = window;
            }

            PresentingContent = content;
            UpdateTheme();
        }), DispatcherPriority.DataBind);
    }

    private void ChildrenOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RaiseEvent(new RoutedEventArgs(ComponentVisibilityChangedEvent));
    }

    private void SettingsOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        UpdateTheme();
    }

    private void UpdateTheme()
    {
        if (Settings == null || !IsOnMainWindow)
            return;
        if (Settings.IsResourceOverridingEnabled)
        {
            Resources[nameof(Settings.MainWindowSecondaryFontSize)] = Settings.MainWindowSecondaryFontSize;
            Resources[nameof(Settings.MainWindowBodyFontSize)] = Settings.MainWindowBodyFontSize;
            Resources[nameof(Settings.MainWindowEmphasizedFontSize)] = Settings.MainWindowEmphasizedFontSize;
            Resources[nameof(Settings.MainWindowLargeFontSize)] = Settings.MainWindowLargeFontSize;
        }
        else
        {
            foreach (var key in (string[])
                     [
                         nameof(Settings.MainWindowSecondaryFontSize), nameof(Settings.MainWindowBodyFontSize),
                         nameof(Settings.MainWindowEmphasizedFontSize), nameof(Settings.MainWindowLargeFontSize)
                     ])
            {
                if (Resources.Contains(key))
                {
                    Resources.Remove(key);
                }
            }
        }

        if (Settings.IsCustomForegroundColorEnabled)
        {
            var brush = new SolidColorBrush(Settings.ForegroundColor);
            SetValue(Control.ForegroundProperty, brush);
            SetValue(TextElement.ForegroundProperty, brush);
            Resources["MahApps.Brushes.ThemeForeground"] = brush;
        }
        else
        {
            if (Resources.Contains("MahApps.Brushes.ThemeForeground"))
            {
                Resources.Remove("MahApps.Brushes.ThemeForeground");
            }
            SetValue(Control.ForegroundProperty, DependencyProperty.UnsetValue);
            SetValue(TextElement.ForegroundProperty, DependencyProperty.UnsetValue);
        }
    }

    public object? PresentingContent
    {
        get => _presentingContent;
        set
        {
            if (Equals(value, _presentingContent)) return;
            _presentingContent = value;
            OnPropertyChanged();
        }
    }

    public ComponentPresenter()
    {
        InitializeComponent();
    }

    private void UpdateWindowRuleState()
    {
        if (HideOnRule)
        {
            CheckHideRule();
            ConditionPulseService.StatusUpdated += ConditionPulseServiceOnStatusUpdated;
        }
        else
        {
            ConditionPulseService.StatusUpdated -= ConditionPulseServiceOnStatusUpdated;
            Visibility = Visibility.Visible;
        }
        UpdateComponentHidState();
    }

    private void ConditionPulseServiceOnStatusUpdated(object? sender, EventArgs e)
    {
        CheckHideRule();
        UpdateComponentHidState();
    }

    private void CheckHideRule()
    {
        if (!HideOnRule)
        {
            return;
        }
        if (ConditionEvaluator.Evaluate(HideCondition))
        {
            Visibility = Visibility.Collapsed;
        }
        else
        {
            Visibility = Visibility.Visible;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void MainContentPresenter_OnComponentVisibilityChanged(object sender, RoutedEventArgs e)
    {
        e.Handled = true;  // 不需要继续向上冒泡
        UpdateComponentHidState();
    }

    private void UpdateComponentHidState()
    {
        var visibleStatePrev = Settings?.IsVisible;
        _isAllComponentsHid = Settings?.Children != null && Settings.Children.FirstOrDefault(x => x.IsVisible) == null;
        if (Settings != null) Settings.IsVisible = Visibility == Visibility.Visible && !_isAllComponentsHid;
        if (Settings?.IsVisible != visibleStatePrev)
        {
            RaiseEvent(new RoutedEventArgs(ComponentVisibilityChangedEvent));
        }
    }

    private void ComponentPresenter_OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateComponentHidState();
    }
}