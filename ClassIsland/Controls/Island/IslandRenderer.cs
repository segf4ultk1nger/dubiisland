using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ClassIsland.Core.Models.Components;
using Squircle;

namespace ClassIsland.Controls.Island;

/// <summary>
/// 岛布局与绘制核心。按 <see cref="IIslandComponent.LineNumber"/> 分行、行内横向排布，一次性画进 DrawingContext。
/// </summary>
public sealed class IslandRenderer
{
    private readonly IslandContext _context;
    private readonly List<IIslandComponent> _components = new();
    private readonly List<IslandLine> _lines = new();
    private readonly Dictionary<int, FadeState> _fades = new();
    private readonly List<(int Line, double Width, double Top, double Height)> _feedBuffer = new();
    private readonly List<(ComponentSettings Key, double X)> _compFeedBuffer = new();

    private Brush _backgroundBrush = Brushes.Black;
    private double _contentHeight;
    private double _contentWidth;
    private double _cachedMaskWidth = -1;
    private double _cachedOverlayWidth = -1;

    /// <summary>几何补间引擎（预览等无动画场景可为 null，此时直接使用目标几何）。</summary>
    public IslandLayoutAnimator? Animator { get; set; }

    /// <summary>是否显示提醒遮罩。</summary>
    public bool IsMaskVisible { get; set; }

    /// <summary>提醒遮罩文本。</summary>
    public string MaskText
    {
        get => _maskText;
        set
        {
            if (_maskText == value)
                return;
            _maskText = value;
            _cachedMaskWidth = -1;
        }
    }

    private string _maskText = "";

    /// <summary>提醒遮罩左图标（Segoe MDL2 字形，空则不画）。</summary>
    public string MaskLeftIcon
    {
        get => _maskLeftIcon;
        set
        {
            if (_maskLeftIcon == value)
                return;
            _maskLeftIcon = value;
            _cachedMaskWidth = -1;
        }
    }

    private string _maskLeftIcon = "";

    /// <summary>提醒遮罩右图标（Segoe MDL2 字形，空则不画）。</summary>
    public string MaskRightIcon
    {
        get => _maskRightIcon;
        set
        {
            if (_maskRightIcon == value)
                return;
            _maskRightIcon = value;
            _cachedMaskWidth = -1;
        }
    }

    private string _maskRightIcon = "";

    /// <summary>是否显示提醒 overlay。</summary>
    public bool IsOverlayVisible { get; set; }

    /// <summary>提醒 overlay 文本。</summary>
    public string OverlayText
    {
        get => _overlayText;
        set
        {
            if (_overlayText == value)
                return;
            _overlayText = value;
            _cachedOverlayWidth = -1;
        }
    }

    private string _overlayText = "";

    /// <summary>是否使用 ClassIsland 2 Fluent 的平行四边形遮罩。</summary>
    public bool UseSlantedMask { get; set; }

    /// <summary>遮罩竖向偏移（矩形遮罩滑入滑出用）。</summary>
    public double MaskOffsetY { get; set; }

    /// <summary>遮罩文字/图标不透明度（矩形底色条不受其影响，底色条只做位移）。</summary>
    public double MaskContentOpacity { get; set; } = 1;

    /// <summary>遮罩文字/图标缩放（Fluent 遮罩用）。</summary>
    public double MaskContentScale { get; set; } = 1;

    /// <summary>Fluent 平行四边形遮罩的 5 个分区展开进度（0=收合，1=展开）。</summary>
    public double[] MaskRegionProgress { get; } = [0, 0, 0, 0, 0];

    /// <summary>重置平行四边形遮罩分区进度。</summary>
    public void ResetMaskRegions(double value)
    {
        for (var i = 0; i < MaskRegionProgress.Length; i++)
            MaskRegionProgress[i] = value;
    }

    /// <summary>overlay 内容不透明度。</summary>
    public double OverlayOpacity { get; set; } = 1;

    /// <summary>岛内容不透明度（提醒时会被压低到 0）。</summary>
    public double ContentOpacity { get; set; } = 1;

    public IslandRenderer(IslandContext context)
    {
        _context = context;
        RefreshStyles();
    }

    public event EventHandler? Invalidated;

    public IReadOnlyList<IIslandComponent> Components => _components;

    /// <summary>内容整体缩放（等价设置里的窗口缩放）。</summary>
    public double Scale => _context.Settings.Scale <= 0 ? 1.0 : _context.Settings.Scale;

    /// <summary>鼠标当前所在行号，-1 表示不在岛上。用于悬停淡出。</summary>
    public int MouseInLine { get; set; } = -1;

    public void Add(IIslandComponent component)
    {
        component.Invalidated += OnComponentInvalidated;
        _components.Add(component);
        Invalidate();
    }

    public bool Remove(IIslandComponent component)
    {
        component.Invalidated -= OnComponentInvalidated;
        if (!_components.Remove(component))
            return false;
        (component as IIslandComponentCleanup)?.Cleanup();
        Invalidate();
        return true;
    }

    /// <summary>清空所有组件（重建组件树前调用）。</summary>
    public void Clear()
    {
        foreach (var component in _components)
        {
            component.Invalidated -= OnComponentInvalidated;
            (component as IIslandComponentCleanup)?.Cleanup();
        }

        _components.Clear();
        Invalidate();
    }

    /// <summary>主题/外观设置变化后重建缓存画笔。</summary>
    public void RefreshStyles()
    {
        var settings = _context.Settings;
        var color = settings.IsCustomBackgroundColorEnabled ? settings.BackgroundColor : _context.ThemeBackground;
        var brush = new SolidColorBrush(color) { Opacity = settings.Opacity };
        brush.Freeze();
        _backgroundBrush = brush;
    }

    public Size Measure(Size availableSize)
    {
        _lines.Clear();
        var desiredWidth = 0.0;
        var spacing = _context.Settings.MainWindowLineVerticalMargin;
        var y = 0.0;
        var first = true;

        foreach (var group in _components.Where(c => c.IsVisible)
                     .GroupBy(c => c.LineNumber)
                     .OrderBy(g => g.Key))
        {
            var components = group.ToArray();
            var sizes = new Size[components.Length];
            var width = 0.0;
            var height = 0.0;
            for (var i = 0; i < components.Length; i++)
            {
                sizes[i] = components[i].Measure(availableSize, _context);
                width += sizes[i].Width;
                height = Math.Max(height, sizes[i].Height);
            }

            if (!first)
                y += spacing;
            first = false;

            _lines.Add(new IslandLine(group.Key, components, sizes, width, height, y));
            desiredWidth = Math.Max(desiredWidth, width);
            y += height;
        }

        _contentHeight = y;
        var left = _context.Settings.MainWindowLeftMargin;
        var right = _context.Settings.MainWindowRightMargin;
        var required = _lines.Count > 0 ? desiredWidth + left + right : 0;
        if (IsMaskVisible)
            required = Math.Max(required, MaskRequiredWidth);
        if (IsOverlayVisible && !string.IsNullOrEmpty(OverlayText))
            required = Math.Max(required, OverlayRequiredWidth);
        _contentWidth = required;

        _feedBuffer.Clear();
        foreach (var line in _lines)
        {
            _feedBuffer.Add((line.LineNumber,
                line.Width + _context.Settings.MainWindowLeftMargin + _context.Settings.MainWindowRightMargin,
                line.Top, line.Height));
        }

        Animator?.Feed(_feedBuffer);

        _compFeedBuffer.Clear();
        var feedLeft = _context.Settings.MainWindowLeftMargin;
        var feedRight = _context.Settings.MainWindowRightMargin;
        var feedHAlign = HorizontalAlign;
        foreach (var line in _lines)
        {
            var lineWidth = line.Width + feedLeft + feedRight;
            var cx = (_contentWidth - lineWidth) * feedHAlign + feedLeft;
            for (var i = 0; i < line.Components.Length; i++)
            {
                if (line.Components[i].MotionKey is { } key)
                    _compFeedBuffer.Add((key, cx));
                cx += line.Sizes[i].Width;
            }
        }

        Animator?.FeedComponents(_compFeedBuffer);

        Animator?.FeedContentWidth(_contentWidth);

        return new Size(_contentWidth, _contentHeight);
    }

    /// <summary>返回前 <paramref name="count"/> 行（不足则全部）底部的 Y 坐标，用于限制预览的最大高度。</summary>
    public double GetLinesBottom(int count)
    {
        if (_lines.Count == 0)
            return 0;
        var line = _lines[Math.Min(count, _lines.Count) - 1];
        return line.Top + line.Height;
    }

    /// <summary>行背景在内容区内的水平对齐（0=左 0.5=中 1=右），跟随全局停靠位置。</summary>
    private double HorizontalAlign => _context.Settings.WindowDockingLocation switch
    {
        1 or 4 => 0.5,
        2 or 5 => 1.0,
        _ => 0.0
    };

    private const double MaskScaleSafety = 1.12; // 遮罩内容有 1.1 的缩放动画，留一点余量

    private double MaskRequiredWidth
    {
        get
        {
            if (_cachedMaskWidth >= 0)
                return _cachedMaskWidth;
            const double gap = 8;
            var total = 0.0;
            if (!string.IsNullOrEmpty(MaskLeftIcon))
                total += MakeIcon(MaskLeftIcon, _context.EmphasizedFontSize + 2, Brushes.White).Width + gap;
            if (!string.IsNullOrEmpty(MaskText))
                total += MakeText(MaskText, _context.EmphasizedFontSize, Brushes.White, FontWeights.Bold).Width;
            if (!string.IsNullOrEmpty(MaskRightIcon))
                total += gap + MakeIcon(MaskRightIcon, _context.EmphasizedFontSize + 2, Brushes.White).Width;
            _cachedMaskWidth = total <= 0
                ? 0
                : total * MaskScaleSafety + _context.Settings.MainWindowLeftMargin + _context.Settings.MainWindowRightMargin;
            return _cachedMaskWidth;
        }
    }

    private double OverlayRequiredWidth
    {
        get
        {
            if (_cachedOverlayWidth >= 0)
                return _cachedOverlayWidth;
            _cachedOverlayWidth = string.IsNullOrEmpty(OverlayText)
                ? 0
                : MakeText(OverlayText, _context.BodyFontSize, new SolidColorBrush(_context.ForegroundColor),
                      FontWeights.Normal).Width
                  + _context.Settings.MainWindowLeftMargin + _context.Settings.MainWindowRightMargin;
            return _cachedOverlayWidth;
        }
    }

    public void Render(DrawingContext drawingContext, Rect bounds)
    {
        var settings = _context.Settings;
        _context.ComponentBounds?.Clear();

        // 1) 底色条：独立于内容淡出。提醒 overlay 阶段 ContentOpacity 归 0 时底色条仍需保留。
        //    每一行是独立的岛：背景宽度贴合该行内容，按全局停靠位置水平对齐。
        var left = settings.MainWindowLeftMargin;
        var right = settings.MainWindowRightMargin;
        var hAlign = HorizontalAlign;
        foreach (var line in _lines)
        {
            var opacity = GetLineOpacity(line.LineNumber);
            var faded = opacity < 0.999;
            if (faded)
                drawingContext.PushOpacity(opacity);

            var lineWidth = line.Width + left + right;
            var displayWidth = Animator?.GetWidth(line.LineNumber, lineWidth) ?? lineWidth;
            var top = Animator?.GetTop(line.LineNumber, line.Top) ?? line.Top;
            var height = Animator?.GetHeight(line.LineNumber, line.Height) ?? line.Height;
            var centerY = top + height / 2;
            var jellyY = Animator?.GetJellyScaleY(line.LineNumber) ?? 1.0;
            var jelly = Math.Abs(jellyY - 1.0) > 0.0001;
            var lineX = (bounds.Width - displayWidth) * hAlign;
            var lineRect = new Rect(lineX, top, displayWidth, height);
            if (jelly)
                drawingContext.PushTransform(new ScaleTransform(1, jellyY, 0, centerY));
            if (lineRect.Width > 0 && lineRect.Height > 0)
            {
                drawingContext.DrawGeometry(_backgroundBrush, null, CreateCornerGeometry(lineRect));
            }
            if (jelly)
                drawingContext.Pop();

            if (faded)
                drawingContext.Pop();
        }

        // 2) 内容组件：受 ContentOpacity 控制（提醒时会被压低到 0）。
        var contentOpaque = ContentOpacity < 1;
        if (contentOpaque)
            drawingContext.PushOpacity(ContentOpacity);
        foreach (var line in _lines)
        {
            var opacity = GetLineOpacity(line.LineNumber);
            var faded = opacity < 0.999;
            if (faded)
                drawingContext.PushOpacity(opacity);

            var lineWidth = line.Width + left + right;
            var displayWidth = Animator?.GetWidth(line.LineNumber, lineWidth) ?? lineWidth;
            var top = Animator?.GetTop(line.LineNumber, line.Top) ?? line.Top;
            var height = Animator?.GetHeight(line.LineNumber, line.Height) ?? line.Height;
            var centerY = top + height / 2;
            var jellyY = Animator?.GetJellyScaleY(line.LineNumber) ?? 1.0;
            var jelly = Math.Abs(jellyY - 1.0) > 0.0001;
            var lineX = (bounds.Width - displayWidth) * hAlign;
            var pillRect = new Rect(lineX, top, displayWidth, height);
            if (jelly)
                drawingContext.PushTransform(new ScaleTransform(1, jellyY, 0, centerY));
            // 仅在几何增长期（显示尺寸 < 目标尺寸）裁剪内容：内容按目标排布，会超出正在生长的 pill。
            // 静止/收缩期内容本就在 pill 内，不裁剪，保持与补间引入前完全一致的绘制。
            var clipped = displayWidth < lineWidth - 0.5 || height < line.Height - 0.5;
            if (clipped)
                drawingContext.PushClip(CreateCornerGeometry(pillRect));

            var x = (bounds.Width - lineWidth) * hAlign + left;
            for (var i = 0; i < line.Components.Length; i++)
            {
                var key = line.Components[i].MotionKey;
                var displayX = key is null ? x : Animator?.GetComponentX(key, x) ?? x;
                var compOpacity = key is null ? 1.0 : Animator?.GetComponentOpacity(key) ?? 1.0;
                var compScale = key is null ? 1.0 : Animator?.GetComponentScale(key) ?? 1.0;
                var slot = new Rect(displayX, top, line.Sizes[i].Width, line.Height);

                var pushOpacity = compOpacity < 0.999;
                if (pushOpacity)
                    drawingContext.PushOpacity(compOpacity);
                var pushScale = Math.Abs(compScale - 1.0) > 0.0001;
                if (pushScale)
                    drawingContext.PushTransform(new ScaleTransform(compScale, compScale,
                        slot.X + slot.Width / 2, slot.Y + slot.Height / 2));

                line.Components[i].Render(drawingContext, slot, _context);

                if (pushScale)
                    drawingContext.Pop();
                if (pushOpacity)
                    drawingContext.Pop();
                x += line.Sizes[i].Width;
            }

            if (clipped)
                drawingContext.Pop();
            if (jelly)
                drawingContext.Pop();

            if (faded)
                drawingContext.Pop();
        }

        if (contentOpaque)
            drawingContext.Pop();

        var displayContentWidth = Animator?.GetContentWidth(_contentWidth) ?? _contentWidth;
        var contentX = (bounds.Width - displayContentWidth) * hAlign;

        // 3) overlay 正文。
        if (IsOverlayVisible && !string.IsNullOrEmpty(OverlayText))
        {
            var overlayOpaque = OverlayOpacity < 1;
            if (overlayOpaque)
                drawingContext.PushOpacity(OverlayOpacity);
            var text = MakeText(OverlayText, _context.BodyFontSize, new SolidColorBrush(_context.ForegroundColor),
                FontWeights.Normal);
            drawingContext.DrawText(text, new Point(
                contentX + Math.Max(0, (displayContentWidth - text.Width) / 2),
                Math.Max(0, (_contentHeight - text.Height) / 2)));
            if (overlayOpaque)
                drawingContext.Pop();
        }

        // 4) 遮罩（底色层 + 内容层）。底色层始终不透明，只做位移/分区展开；内容层单独淡入。
        if (IsMaskVisible)
        {
            var maskRect = new Rect(contentX, 0, displayContentWidth, _contentHeight);
            drawingContext.PushClip(CreateCornerGeometry(maskRect));
            var accent = new SolidColorBrush(_context.AccentColor);
            if (UseSlantedMask)
                DrawSlantedMask(drawingContext, maskRect, accent);
            else
                drawingContext.DrawGeometry(accent, null,
                    CreateCornerGeometry(new Rect(contentX, MaskOffsetY, displayContentWidth, _contentHeight)));
            DrawMaskContent(drawingContext, maskRect);
            drawingContext.Pop();
        }
    }

    /// <summary>绘制遮罩文字/图标（独立于遮罩底色，支持不透明度与缩放）。</summary>
    private void DrawMaskContent(DrawingContext drawingContext, Rect bounds)
    {
        var opaque = MaskContentOpacity < 1;
        if (opaque)
            drawingContext.PushOpacity(MaskContentOpacity);
        var scaled = Math.Abs(MaskContentScale - 1.0) > 0.0001;
        if (scaled)
        {
            drawingContext.PushTransform(new ScaleTransform(MaskContentScale, MaskContentScale,
                bounds.X + bounds.Width / 2, _contentHeight / 2));
        }

        var centerY = _contentHeight / 2 + (UseSlantedMask ? 0 : MaskOffsetY);
        const double gap = 8;
        var text = string.IsNullOrEmpty(MaskText)
            ? null
            : MakeText(MaskText, _context.EmphasizedFontSize, Brushes.White, FontWeights.Bold);
        var leftIcon = string.IsNullOrEmpty(MaskLeftIcon)
            ? null
            : MakeIcon(MaskLeftIcon, _context.EmphasizedFontSize + 2, Brushes.White);
        var rightIcon = string.IsNullOrEmpty(MaskRightIcon)
            ? null
            : MakeIcon(MaskRightIcon, _context.EmphasizedFontSize + 2, Brushes.White);

        var total = (leftIcon?.Width ?? 0) + (leftIcon != null ? gap : 0)
                    + (text?.Width ?? 0)
                    + (rightIcon != null ? gap : 0) + (rightIcon?.Width ?? 0);
        var x = bounds.X + Math.Max(0, (bounds.Width - total) / 2);
        if (leftIcon != null)
        {
            drawingContext.DrawText(leftIcon, new Point(x, centerY - leftIcon.Height / 2));
            x += leftIcon.Width + gap;
        }

        if (text != null)
        {
            drawingContext.DrawText(text, new Point(x, centerY - text.Height / 2));
            x += text.Width + gap;
        }

        if (rightIcon != null)
            drawingContext.DrawText(rightIcon, new Point(x, centerY - rightIcon.Height / 2));

        if (scaled)
            drawingContext.Pop();
        if (opaque)
            drawingContext.Pop();
    }

    /// <summary>按当前圆角设置生成矩形几何：启用超椭圆时用 Figma 风格 squircle，否则普通圆角矩形。</summary>
    private Geometry CreateCornerGeometry(Rect rect)
    {
        var settings = _context.Settings;
        return settings.IsSquircleEnabled
            ? SmoothGeometry.Create(rect, new SmoothCornerRadius(settings.RadiusX, settings.SquircleSmoothing))
            : new RectangleGeometry(rect, settings.RadiusX, settings.RadiusX);
    }

    /// <summary>绘制 ClassIsland 2 Fluent 的平行四边形遮罩（5 个分区，按进度展开/收合）。</summary>
    private void DrawSlantedMask(DrawingContext drawingContext, Rect bounds, Brush brush)
    {
        var h = _contentHeight;
        if (h <= 0)
            return;

        var offset = Math.Tan(Math.PI / 6.0) * h;
        var totalWidth = bounds.Width + offset;
        var weight = new[] { 0.0, 0.1, 0.2, 0.4, 0.2, 0.1 };
        var startX = new double[6];
        var accumulated = 0.0;
        for (var i = 0; i < 6; i++)
        {
            accumulated += totalWidth * weight[i];
            startX[i] = accumulated;
        }

        for (var i = 0; i < 5; i++)
        {
            var progress = Math.Min(1.0, Math.Max(0.0, MaskRegionProgress[i]));
            var center = (startX[i] + startX[i + 1]) / 2.0;
            var currentWidth = (totalWidth * weight[i + 1] + 0.5) * progress;
            if (currentWidth < 0.0001)
                continue;

            var p1 = new Point(bounds.X + center - currentWidth / 2, 0);
            var p2 = new Point(bounds.X + center + currentWidth / 2, 0);
            var p3 = new Point(bounds.X + center + currentWidth / 2 - offset, h);
            var p4 = new Point(bounds.X + center - currentWidth / 2 - offset, h);

            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                g.BeginFigure(p1, true, true);
                g.LineTo(p2, true, false);
                g.LineTo(p3, true, false);
                g.LineTo(p4, true, false);
            }

            geometry.Freeze();
            drawingContext.DrawGeometry(brush, null, geometry);
        }
    }

    private FormattedText MakeText(string text, double size, Brush brush, FontWeight weight)
        => new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(_context.FontFamily, FontStyles.Normal, weight, FontStretches.Normal),
            size, brush, _context.PixelsPerDip);

    private FormattedText MakeIcon(string glyph, double size, Brush brush)
        => new(glyph, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface("MahApps.Metro.IconPacks.RemixIcon"), size, brush, _context.PixelsPerDip);

    /// <summary>取组件在内容自然坐标中的槽位矩形（用于预览聚焦）。</summary>
    public bool TryGetComponentBounds(ComponentSettings component, out Rect bounds)
    {
        if (_context.ComponentBounds != null && _context.ComponentBounds.TryGetValue(component, out bounds))
            return true;
        bounds = Rect.Empty;
        return false;
    }

    /// <summary>取整行（岛背景）在内容自然坐标中的矩形。</summary>
    public bool TryGetLineBounds(int lineNumber, out Rect bounds)
    {
        var left = _context.Settings.MainWindowLeftMargin;
        var right = _context.Settings.MainWindowRightMargin;
        var hAlign = HorizontalAlign;
        foreach (var line in _lines)
        {
            if (line.LineNumber != lineNumber)
                continue;
            var lineWidth = line.Width + left + right;
            bounds = new Rect((_contentWidth - lineWidth) * hAlign, line.Top, lineWidth, line.Height);
            return true;
        }

        bounds = Rect.Empty;
        return false;
    }

    /// <summary>命中测试：给定自然坐标，返回所在行号，未命中返回 -1。</summary>
    public int HitTestLine(Point point)
    {
        var left = _context.Settings.MainWindowLeftMargin;
        var right = _context.Settings.MainWindowRightMargin;
        var hAlign = HorizontalAlign;
        foreach (var line in _lines)
        {
            if (point.Y < line.Top || point.Y > line.Top + line.Height)
                continue;
            var lineWidth = line.Width + left + right;
            var lineX = (_contentWidth - lineWidth) * hAlign;
            if (point.X >= lineX && point.X <= lineX + lineWidth)
                return line.LineNumber;
        }

        return -1;
    }

    public void Invalidate() => Invalidated?.Invoke(this, EventArgs.Empty);

    private void OnComponentInvalidated(object? sender, EventArgs e) => Invalidate();

    /// <summary>按当前鼠标所在行和设置，为每行设定淡出目标；返回是否有行需要动画。</summary>
    public bool UpdateFadeTargets()
    {
        var settings = _context.Settings;
        var animating = false;
        foreach (var line in _lines)
        {
            if (!_fades.TryGetValue(line.LineNumber, out var state))
            {
                state = new FadeState();
                _fades[line.LineNumber] = state;
            }

            var faded = settings.IsMouseInFadingEnabled &&
                        ((line.LineNumber == MouseInLine) ^ settings.IsMouseInFadingReversed);
            var target = faded ? 0.05 : 1.0;
            if (Math.Abs(state.To - target) > 0.0001)
            {
                state.From = state.Current;
                state.To = target;
                state.StartSeconds = -1;
                state.Duration = 0.15;
                state.Easing = target < state.From
                    ? new CircleEase { EasingMode = EasingMode.EaseOut }
                    : new CircleEase { EasingMode = EasingMode.EaseIn };
            }

            if (Math.Abs(state.Current - state.To) > 0.0001)
                animating = true;
        }

        return animating;
    }

    /// <summary>推进淡出补间；返回是否仍在动画中。</summary>
    public bool TickFades(double nowSeconds)
    {
        var animating = false;
        foreach (var state in _fades.Values)
        {
            if (Math.Abs(state.Current - state.To) <= 0.0001)
            {
                state.Current = state.To;
                continue;
            }

            if (state.StartSeconds < 0)
                state.StartSeconds = nowSeconds;
            var u = state.Duration <= 0 ? 1 : Math.Min(1, (nowSeconds - state.StartSeconds) / state.Duration);
            var eased = state.Easing?.Ease(u) ?? u;
            state.Current = state.From + (state.To - state.From) * eased;
            if (u >= 1)
                state.Current = state.To;
            else
                animating = true;
        }

        return animating;
    }

    private double GetLineOpacity(int lineNumber)
        => _fades.TryGetValue(lineNumber, out var state) ? state.Current : 1.0;

    private sealed class FadeState
    {
        public double Current = 1;
        public double From = 1;
        public double To = 1;
        public double StartSeconds = -1;
        public double Duration = 0.15;
        public IEasingFunction? Easing;
    }

    private sealed class IslandLine
    {
        public IslandLine(int lineNumber, IIslandComponent[] components, Size[] sizes, double width, double height,
            double top)
        {
            LineNumber = lineNumber;
            Components = components;
            Sizes = sizes;
            Width = width;
            Height = height;
            Top = top;
        }

        public int LineNumber { get; }

        public IIslandComponent[] Components { get; }

        public Size[] Sizes { get; }

        /// <summary>该行内容宽度（含组件外边距，不含全局左右边距）。</summary>
        public double Width { get; }

        public double Height { get; }

        /// <summary>该行在内容区内的顶部 Y 坐标（已含行间距）。</summary>
        public double Top { get; }
    }
}
