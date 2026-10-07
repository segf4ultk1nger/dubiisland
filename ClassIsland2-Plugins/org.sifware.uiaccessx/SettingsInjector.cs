using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Controls;
using ClassIsland.Core.Controls.NavHyperlink;
using FluentAvalonia.UI.Controls;

namespace Org.Sifware.UiAccessX;

/// <summary>
/// 把 UIAccess 相关设置注入到内置设置界面中，使其与原生设置项外观一致：
/// <list type="bullet">
/// <item>主界面 &gt; 窗口 &gt; “窗口层级”下追加 UIAccess 开关；</item>
/// <item>通用 &gt; 基本 &gt; “开机自启”右侧追加管理员自启按钮；</item>
/// <item>设置窗口右上角菜单“重启到恢复模式”上方追加“以管理员身份重启”；</item>
/// <item>关于 &gt; 鸣谢 文末追加一行 LegacyIsland 推广。</item>
/// </list>
/// 依赖内置设置页的可视结构（<c>SettingsExpander.Header</c> 文案），若上游改版导致找不到目标，注入会自动跳过，不会影响其他功能。
/// </summary>
internal static class SettingsInjector
{
    private static bool _started;
    private static readonly HashSet<SettingsPageBase> InjectedPages = new();
    private static readonly HashSet<Window> InjectedWindows = new();

    public static void Start()
    {
        if (_started || !OperatingSystem.IsWindows())
        {
            return;
        }

        _started = true;
        Control.LoadedEvent.AddClassHandler<SettingsPageBase>((page, _) => Inject(page));
        Control.LoadedEvent.AddClassHandler<Window>((window, _) => InjectWindowMenu(window));
    }

    private static void Inject(SettingsPageBase page)
    {
        // 页面可能被缓存复用，Loaded 会多次触发，只注入一次。
        if (!InjectedPages.Add(page))
        {
            return;
        }

        // 注入失败不应影响内置设置页本身的显示。
        try
        {
            switch (page.Tag as string)
            {
                case "window":
                    InjectWindowLayer(page);
                    break;
                case "general":
                    InjectGeneralAutoStart(page);
                    break;
                case "about":
                    InjectAbout(page);
                    break;
            }
        }
        catch
        {
            // 忽略：上游结构变化导致注入失败时静默跳过。
        }
    }

    private static void InjectWindowLayer(SettingsPageBase page)
    {
        var expander = FindExpander(page, "窗口层级");
        if (expander == null)
        {
            return;
        }

        expander.Items.Add(MakeToggleItem(
            "\uEF4F",
            "UIAccess 超级置顶",
            "启用后 ClassIsland 可置于开始菜单和系统界面之上。开启后会尝试重启至管理员身份运行 ClassIsland。",
            () => UiAccessX.Settings.EnableUiAccess,
            v =>
            {
                UiAccessX.Settings.EnableUiAccess = v;
                UiAccessX.SaveSettings();
            }));
    }

    private static void InjectGeneralAutoStart(SettingsPageBase page)
    {
        var expander = FindExpander(page, "开机自启");
        if (expander == null)
        {
            return;
        }

        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };

        // 把原有的“开机自启”开关移动到新面板中，保持其原有绑定不变。
        if (expander.Footer is Control footer)
        {
            expander.Footer = null;
            panel.Children.Add(footer);
        }

        panel.Children.Add(MakeAdminAutoStartButton());
        expander.Footer = panel;
    }

    private static void InjectAbout(SettingsPageBase page)
    {
        var textBlock = page.GetLogicalDescendants()
            .OfType<TextBlock>()
            .FirstOrDefault(x => (x.Inlines?.Text ?? "").Contains("感谢其他使用的第三方库"));
        if (textBlock?.Inlines is not { } inlines)
        {
            return;
        }

        var link = new NavHyperlink
        {
            NavTarget = "https://gitlab.com/dubi906w/dubiisland",
            Content = "LegacyIsland"
        };
        TextElement.SetFontWeight(link, FontWeight.Bold);

        inlines.Add(new LineBreak());
        inlines.Add(new Run
        {
            Text = "如果你想使用 ClassIsland 1.x 版本，不妨来试试 SIFWARE 的 ",
            FontWeight = FontWeight.Bold
        });
        inlines.Add(new InlineUIContainer(link));
        inlines.Add(new Run
        {
            Text = "？比原版 1.x 更好用更流畅！",
            FontWeight = FontWeight.Bold
        });
    }

    private static SettingsExpander? FindExpander(SettingsPageBase page, string header) =>
        page.GetLogicalDescendants()
            .OfType<SettingsExpander>()
            .FirstOrDefault(x => string.Equals(x.Header as string, header, StringComparison.Ordinal));

    private static SettingsExpanderItem MakeToggleItem(
        string glyph, string content, string description, Func<bool> get, Action<bool> set)
    {
        var toggle = new ToggleSwitch
        {
            IsChecked = get(),
            VerticalAlignment = VerticalAlignment.Center
        };
        toggle.PropertyChanged += (_, e) =>
        {
            if (e.Property == ToggleSwitch.IsCheckedProperty)
            {
                set(toggle.IsChecked == true);
            }
        };

        return new SettingsExpanderItem
        {
            IconSource = new FluentIconSource(glyph),
            Content = content,
            Description = description,
            Footer = toggle
        };
    }

    private static Button MakeAdminAutoStartButton()
    {
        var button = new Button { VerticalAlignment = VerticalAlignment.Center };
        UpdateAdminAutoStartButton(button);

        button.Click += (_, _) =>
        {
            AdminStartup.SetEnabled(!AdminStartup.IsRegistered());
            UpdateAdminAutoStartButton(button);
        };

        return button;
    }

    private static void UpdateAdminAutoStartButton(Button button)
    {
        var registered = AdminStartup.IsRegistered();
        button.Content = new IconText
        {
            // 按钮为“自启”动作：shield_task；按钮为“取消”动作：shield_prohibited
            Glyph = registered ? "\uEF69" : "\uEF6F",
            Text = registered ? "取消管理员自启" : "以管理员身份自启"
        };
        ToolTip.SetTip(button, registered
            ? "已创建计划任务，登录时将以最高权限启动（无 UAC 弹窗）。点击移除。"
            : "创建计划任务，在登录时以管理员身份启动 ClassIsland（无需密码，无 UAC 弹窗）。");
    }

    private static void InjectWindowMenu(Window window)
    {
        // 只处理设置窗口；插件无法在编译期引用 ClassIsland.Views.SettingsWindowNew，故按类型名匹配。
        if (window.GetType().Name != "SettingsWindowNew")
        {
            return;
        }

        // 设置窗口是单例，Loaded 可能多次触发，只注入一次。
        if (!InjectedWindows.Add(window))
        {
            return;
        }

        try
        {
            foreach (var button in window.GetLogicalDescendants().OfType<Button>())
            {
                if (button.Flyout is not MenuFlyout flyout)
                {
                    continue;
                }

                var target = flyout.Items.OfType<MenuItem>()
                    .FirstOrDefault(x => string.Equals(x.Header as string, "重启到恢复模式", StringComparison.Ordinal));
                if (target == null)
                {
                    continue;
                }

                flyout.Items.Insert(flyout.Items.IndexOf(target), CreateRestartAsAdminMenuItem());
                return;
            }
        }
        catch
        {
            // 忽略：结构变化导致注入失败时静默跳过。
        }
    }

    private static MenuItem CreateRestartAsAdminMenuItem()
    {
        var elevated = UiAccessX.IsElevated();
        var item = new MenuItem
        {
            Header = "以管理员身份重启",
            Icon = new FluentIcon("\uE0B5"),
            IsEnabled = !elevated
        };
        ToolTip.SetTip(item, elevated
            ? "当前已以管理员身份运行。"
            : "以管理员身份重新启动 ClassIsland。");
        item.Click += (_, _) => AdminStartup.RestartAsAdmin();
        return item;
    }
}
