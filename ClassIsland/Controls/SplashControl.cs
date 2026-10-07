using System;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Services;

namespace ClassIsland.Controls;

/// <summary>
/// 首屏自绘控件：完全由 <see cref="OnRender"/> 绘制，无 XAML、无控件树、无 Storyboard/资源样式开销。
/// 显示/隐藏动画照搬自绘岛（弹性放大淡入 / 收缩淡出）。
/// </summary>
public sealed class SplashControl : FrameworkElement
{
    private const double PanelWidth = 400;
    private const double PanelHeight = 100;
    private const double Padding = 17;
    private const double LogoHeight = 32;
    private const double ProgressBarHeight = 4;
    private const double ProgressBarInset = 1;

    private const double ShowScaleMin = 0.89;
    private const double ShowAnimatedDuration = 0.47;
    private const double ShowFadePortion = 0.78;
    private const double HideAnimatedDuration = 0.13;
    private const double ElasticConst = 2 * Math.PI / 0.3;
    private const double ElasticConst2 = 0.3 / 4;
    private static readonly double ExpoOffset = Math.Pow(2, -10);
    private static readonly double ElasticOffsetHalf =
        Math.Pow(2, -10) * Math.Sin((0.5 - ElasticConst2) * ElasticConst);

    private static readonly Brush BackgroundBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x2E, 0x2E, 0x2E)));
    private static readonly Brush ForegroundBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xFA, 0xFA, 0xFA)));
    private static readonly Brush DimBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x80, 0xFA, 0xFA, 0xFA)));
    private static readonly Brush ProgressTrackBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)));
    private static readonly Pen DimRingPen = Freeze(new Pen(DimBrush, 2));
    private static readonly Pen SpinPen = Freeze(new Pen(ForegroundBrush, 2));
    private static readonly Typeface Typeface = new(
        new FontFamily("HarmonyOS Sans SC, Microsoft YaHei UI, SimHei, Segoe UI"),
        FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    private readonly ISplashService _splashService;
    private readonly SettingsService _settingsService;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private Brush _accentBrush = Freeze(new SolidColorBrush(Colors.DodgerBlue));
    private Pen _borderPen = Freeze(new Pen(new SolidColorBrush(Colors.DodgerBlue), 1));
    private ImageSource? _logo;
    private string? _logoKey;

    private double _displayedProgress;
    private double _lastFrame;

    private bool _showAnimActive;
    private double? _showAnimStart;
    private double _showFromScale = 1;
    private double _showFromOpacity = 1;
    private bool _hideAnimActive;
    private double? _hideAnimStart;
    private double _hideFromScale = 1;
    private double _hideFromOpacity = 1;

    private volatile bool _closing;
    private bool _closeRequested;
    private bool _hooked;

    public SplashControl()
    {
        _splashService = App.GetService<ISplashService>();
        _settingsService = App.GetService<SettingsService>();
        _splashService.SplashEnded += OnSplashEnded;

        RenderTransformOrigin = new Point(0.5, 0.5);
        RenderTransform = new ScaleTransform(1, 1);

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ResolveAccent();
        _clock.Restart();
        _lastFrame = 0;

        // 与自绘岛一致：出现动画前先落到起点，避免全尺寸闪一帧。
        if (_hideAnimActive)
        {
            (_showFromScale, _showFromOpacity) = GetVisual();
        }
        else
        {
            _showFromScale = ShowScaleMin;
            _showFromOpacity = 0;
            ApplyVisual(_showFromScale, _showFromOpacity);
        }

        _hideAnimActive = false;
        _showAnimActive = true;
        _showAnimStart = null;

        if (!_hooked)
        {
            CompositionTarget.Rendering += OnRendering;
            _hooked = true;
        }

        InvalidateVisual();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_hooked)
        {
            CompositionTarget.Rendering -= OnRendering;
            _hooked = false;
        }

        _splashService.SplashEnded -= OnSplashEnded;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var now = _clock.Elapsed.TotalSeconds;
        UpdateProgress(now);

        if (_closing && !_closeRequested && !_hideAnimActive && _displayedProgress >= 100)
        {
            StartHide();
        }

        UpdateShowHideAnimation(now);
        InvalidateVisual();
    }

    private void OnSplashEnded(object? sender, EventArgs e) => _closing = true;

    private void StartHide()
    {
        // 出现动画中被打断 → 从当前视觉续接；否则从静止态收起。
        (_hideFromScale, _hideFromOpacity) = _showAnimActive ? GetVisual() : (1.0, 1.0);
        _showAnimActive = false;
        _hideAnimActive = true;
        _hideAnimStart = null;
    }

    private void UpdateShowHideAnimation(double now)
    {
        if (_showAnimActive)
        {
            _showAnimStart ??= now;
            var t = now - _showAnimStart.Value;
            var p = t / ShowAnimatedDuration;
            if (p >= 1)
            {
                ApplyVisual(1, 1);
                _showAnimActive = false;
            }
            else
            {
                ApplyVisual(
                    _showFromScale + (1 - _showFromScale) * OutElasticHalf(p),
                    _showFromOpacity + (1 - _showFromOpacity) * OutExpo(Math.Min(1, p / ShowFadePortion)));
            }
        }

        if (_hideAnimActive)
        {
            _hideAnimStart ??= now;
            var t = now - _hideAnimStart.Value;
            var p = t / HideAnimatedDuration;
            if (p >= 1)
            {
                ApplyVisual(ShowScaleMin, 0);
                _hideAnimActive = false;
                _closeRequested = true;
                var window = Window.GetWindow(this);
                Dispatcher.BeginInvoke(new Action(() => window?.Close()));
            }
            else
            {
                ApplyVisual(
                    _hideFromScale + (ShowScaleMin - _hideFromScale) * OutExpo(p),
                    _hideFromOpacity * (1 - CubicOut(p)));
            }
        }
    }

    private void ApplyVisual(double scale, double opacity)
    {
        if (RenderTransform is ScaleTransform transform)
        {
            transform.ScaleX = scale;
            transform.ScaleY = scale;
        }

        Opacity = opacity;
    }

    private (double Scale, double Opacity) GetVisual()
    {
        var scale = RenderTransform is ScaleTransform transform ? transform.ScaleX : 1.0;
        return (scale, Opacity);
    }

    private void ResolveAccent()
    {
        try
        {
            if (TryFindResource("MahApps.Brushes.Accent") is SolidColorBrush brush)
            {
                var copy = (Brush)brush.CloneCurrentValue();
                copy.Freeze();
                _accentBrush = copy;
                _borderPen = Freeze(new Pen(copy, 1));
            }
        }
        catch
        {
            // 主题资源不可用时保留默认强调色。
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 1 || h <= 1)
        {
            return;
        }

        var ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var t = _clock.Elapsed.TotalSeconds;

        EnsureLogo();

        // 面板固定 400×100 居中绘制，四周留出弹性过冲（约 3.4px）的余量，避免出现动画被窗口裁切。
        var panel = new Rect((w - PanelWidth) / 2, (h - PanelHeight) / 2, PanelWidth, PanelHeight);

        dc.PushClip(new RectangleGeometry(panel));
        // 旧版 Border.Opacity=0.8，作用于整个面板（含文字/进度条）。
        dc.PushOpacity(0.8);
        dc.DrawRectangle(BackgroundBrush, null, new Rect(panel.X, panel.Y, panel.Width, panel.Height));

        DrawLogoRow(dc, panel.X + Padding, panel.Y + Padding, ppd);
        DrawStatusRow(dc, panel, t, ppd);

        if (_settingsService.Settings.ShowSplashVersionInfo)
        {
            var version = Format($"LegacyIsland {App.AppVersionLong}", 9, DimBrush, ppd);
            dc.DrawText(version, new Point(panel.X + 4, panel.Bottom - ProgressBarHeight - 3 - version.Height));
        }

        // 收到边框内侧，避免轨道/前景盖住面板的 1px 边框。
        var progressRect = new Rect(panel.X + ProgressBarInset, panel.Bottom - ProgressBarInset - ProgressBarHeight,
            panel.Width - ProgressBarInset * 2, ProgressBarHeight);
        dc.DrawRectangle(ProgressTrackBrush, null, progressRect);
        dc.DrawRectangle(_accentBrush, null,
            new Rect(progressRect.X, progressRect.Y, progressRect.Width * Clamp01(_displayedProgress / 100), progressRect.Height));

        // 边框最后绘制，保证其颜色不被背景/进度条盖住。
        dc.DrawRectangle(null, _borderPen, new Rect(panel.X + 0.5, panel.Y + 0.5, panel.Width - 1, panel.Height - 1));

        dc.Pop();
        dc.Pop();
    }

    private void DrawLogoRow(DrawingContext dc, double x, double y, double ppd)
    {
        var logoWidth = 0.0;
        if (_logo is { Width: > 0, Height: > 0 })
        {
            logoWidth = LogoHeight * (_logo.Width / _logo.Height);
            dc.DrawImage(_logo, new Rect(x, y, logoWidth, LogoHeight));
        }

        if (string.IsNullOrEmpty(_settingsService.Settings.SplashCustomLogoSource))
        {
            var title = Format("LegacyIsland", 20, ForegroundBrush, ppd, FontWeights.Medium);
            dc.DrawText(title, new Point(x + logoWidth + 8, y + (LogoHeight - title.Height) / 2));
        }
    }

    private void DrawStatusRow(DrawingContext dc, Rect panel, double t, double ppd)
    {
        const double spinnerSize = 20;
        var x = panel.X + Padding;
        var statusY = panel.Bottom - Padding - spinnerSize;

        DrawSpinner(dc, new Point(x + spinnerSize / 2, statusY + spinnerSize / 2), spinnerSize / 2 - 1, t);

        var status = Format(_splashService.SplashStatus, 16, ForegroundBrush, ppd);
        dc.DrawText(status, new Point(x + spinnerSize + 4, statusY + (spinnerSize - status.Height) / 2));
    }

    private static void DrawSpinner(DrawingContext dc, Point center, double radius, double t)
    {
        dc.DrawEllipse(null, DimRingPen, center, radius, radius);
        var start = t * 300 % 360;
        dc.DrawGeometry(null, SpinPen, BuildArc(center, radius, start, 100));
    }

    private static Geometry BuildArc(Point center, double radius, double startAngle, double sweepAngle)
    {
        var figure = new PathFigure
        {
            StartPoint = PointOnCircle(center, radius, startAngle),
            IsClosed = false,
            IsFilled = false
        };
        figure.Segments.Add(new ArcSegment(PointOnCircle(center, radius, startAngle + sweepAngle),
            new Size(radius, radius), 0, sweepAngle > 180, SweepDirection.Clockwise, true));
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    private static Point PointOnCircle(Point center, double radius, double angleDegrees)
    {
        var radians = angleDegrees * Math.PI / 180;
        return new Point(center.X + radius * Math.Cos(radians), center.Y + radius * Math.Sin(radians));
    }

    private void UpdateProgress(double now)
    {
        var dt = now - _lastFrame;
        _lastFrame = now;
        if (dt is < 0 or > 0.1)
        {
            dt = dt < 0 ? 0 : 0.1;
        }

        if (_closing)
        {
            // 结束时快速补到 100%（等价旧版 0.1s 动画）。
            _displayedProgress = Math.Min(100, _displayedProgress + dt * 1000);
            return;
        }

        var target = _splashService.CurrentProgress;
        _displayedProgress += (target - _displayedProgress) * (1 - Math.Exp(-dt * 10));
    }

    private void EnsureLogo()
    {
        var source = _settingsService.Settings.SplashCustomLogoSource;
        var key = source ?? "";
        if (key == _logoKey)
        {
            return;
        }

        _logoKey = key;
        _logo = LoadLogo(key);
    }

    private static ImageSource? LoadLogo(string source)
    {
        try
        {
            if (string.IsNullOrEmpty(source))
            {
                return CreateBitmap(new Uri("pack://application:,,,/Assets/AppLogo.png", UriKind.Absolute));
            }

            // 与旧版 XAML 绑定 Image.Source 的行为保持一致。
            return (ImageSource)new ImageSourceConverter().ConvertFromString(source);
        }
        catch
        {
            try
            {
                return CreateBitmap(new Uri(source, UriKind.RelativeOrAbsolute));
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>立即解码，保证首帧就能拿到尺寸。</summary>
    private static BitmapImage CreateBitmap(Uri uri)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = uri;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private FormattedText Format(string text, double size, Brush brush, double pixelsPerDip,
        FontWeight? weight = null)
    {
        var typeface = weight == null
            ? Typeface
            : new Typeface(Typeface.FontFamily, Typeface.Style, weight.Value, Typeface.Stretch);
        return new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface,
            size, brush, pixelsPerDip);
    }

    private static double Clamp01(double value) => value < 0 ? 0 : value > 1 ? 1 : value;

    private static double OutExpo(double t)
    {
        t = Clamp01(t);
        return -Math.Pow(2, -10 * t) + 1 + ExpoOffset * t;
    }

    private static double OutElasticHalf(double t)
    {
        t = Clamp01(t);
        return Math.Pow(2, -10 * t) * Math.Sin((0.5 * t - ElasticConst2) * ElasticConst) + 1 -
               ElasticOffsetHalf * t;
    }

    private static double CubicOut(double t)
    {
        t = Clamp01(t);
        var u = 1 - t;
        return 1 - u * u * u;
    }

    private static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
