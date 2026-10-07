using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Reactive;
using Avalonia.VisualTree;
using ClassIsland.Core;
using ClassIsland.Core.Assists;

namespace Org.Sifware.UiAccessX.Squircle;

/// <summary>
/// 运行期把主界面「岛」的圆角背景换成超椭圆（squircle）。
/// 做法：找到所有 <c>Border.line-background</c>（整体/分体两种模式下岛背景都用这个类），
/// 把它原来的角形绘制交给 <see cref="SquircleBorder"/> 自绘（保留阴影、描边、主题填充色），
/// 原 Border 只保留布局。开启/关闭或平滑度、主题变化时整体刷新。
/// </summary>
internal static class SquircleRenderer
{
    private static bool _started;
    private static readonly Dictionary<Border, SquircleState> Applied = new();

    public static void Start()
    {
        if (_started || !OperatingSystem.IsWindows())
        {
            return;
        }

        _started = true;
        Control.LoadedEvent.AddClassHandler<Control>((control, _) => OnLoaded(control));

        try
        {
            AppBase.Current.AppStarted += (_, _) => Refresh();
        }
        catch
        {
            // AppBase 在极早期可能不可用，忽略。
        }

        if (Application.Current is { } app)
        {
            app.ActualThemeVariantChanged += (_, _) => Refresh();
        }
    }

    /// <summary>按当前设置重新应用或移除超椭圆渲染。</summary>
    public static void Refresh()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            return;
        }

        if (!UiAccessX.Settings.IsSquircleEnabled)
        {
            RemoveAll();
            return;
        }

        foreach (var window in desktop.Windows)
        {
            foreach (var border in window.GetVisualDescendants().OfType<Border>())
            {
                if (border.Classes.Contains("line-background"))
                {
                    Apply(border);
                }
            }
        }

        foreach (var state in Applied.Values)
        {
            state.Update();
        }
    }

    private static void OnLoaded(Control control)
    {
        if (UiAccessX.Settings.IsSquircleEnabled &&
            control is Border border && border.Classes.Contains("line-background"))
        {
            Apply(border);
        }
    }

    private static void Apply(Border border)
    {
        if (Applied.ContainsKey(border) || border.Child != null)
        {
            return;
        }

        try
        {
            var state = new SquircleState(border);
            Applied[border] = state;
            border.DetachedFromVisualTree += OnBorderDetached;
            state.Apply();
        }
        catch
        {
            // 上游结构变化导致接管失败时静默跳过。
        }
    }

    private static void OnBorderDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is Border border)
        {
            Remove(border);
        }
    }

    private static void Remove(Border border)
    {
        if (Applied.Remove(border, out var state))
        {
            border.DetachedFromVisualTree -= OnBorderDetached;
            state.Restore();
        }
    }

    private static void RemoveAll()
    {
        foreach (var (border, state) in Applied.ToList())
        {
            border.DetachedFromVisualTree -= OnBorderDetached;
            state.Restore();
        }

        Applied.Clear();
    }

    /// <summary>接管单个背景 Border 的绘制，并同步半径/主题色。</summary>
    private sealed class SquircleState
    {
        private readonly Border _border;
        private readonly SquircleBorder _overlay = new();
        private readonly BoxShadows _shadow;
        private readonly IBrush? _originalFill;
        private readonly IBrush? _originalStroke;
        private readonly double _strokeThickness;

        public SquircleState(Border border)
        {
            _border = border;
            _shadow = border.BoxShadow;
            _originalFill = border.Background;
            _originalStroke = border.BorderBrush;
            _strokeThickness = border.BorderThickness.Left;
        }

        public void Apply()
        {
            _overlay.IsHitTestVisible = false;

            // 原 Border 保留布局，绘制交给 overlay。
            _border.Child = _overlay;
            _border.SetValue(Border.BackgroundProperty, null);
            _border.SetValue(Border.BorderThicknessProperty, default(Thickness));
            _border.SetValue(Border.BoxShadowProperty, default(BoxShadows));
            _border.SetValue(Border.ClipToBoundsProperty, false);

            // 半径随设置（MainWindowStylesAssist.CornerRadius ← Settings.RadiusX）动态变化。
            _border.GetObservable(MainWindowStylesAssist.CornerRadiusProperty)
                .Subscribe(new AnonymousObserver<double>(value => _overlay.CornerRadius = value));

            Update();
        }

        public void Update()
        {
            _overlay.Smoothing = UiAccessX.Settings.SquircleSmoothing;
            _overlay.Fill = ResolveFill();
            _overlay.Stroke = ResolveStroke();
            _overlay.StrokeThickness = _strokeThickness > 0 ? _strokeThickness : 1.0;
            _overlay.BoxShadow = _shadow;
        }

        public void Restore()
        {
            _border.Child = null;
            _border.ClearValue(Border.BackgroundProperty);
            _border.ClearValue(Border.BorderThicknessProperty);
            _border.ClearValue(Border.BoxShadowProperty);
            _border.ClearValue(Border.ClipToBoundsProperty);
        }

        private IBrush? ResolveFill()
        {
            if (MainWindowStylesAssist.GetIsCustomBackgroundColorEnabled(_border))
            {
                return new SolidColorBrush(MainWindowStylesAssist.GetBackgroundColor(_border));
            }

            return _border.TryFindResource("SolidBackgroundFillColorSecondaryBrush", out var value)
                ? value as IBrush
                : _originalFill;
        }

        private IBrush? ResolveStroke() =>
            _border.TryFindResource("ControlElevationBorderBrush", out var value)
                ? value as IBrush
                : _originalStroke;
    }
}
