using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

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

    private Brush _backgroundBrush = Brushes.Black;
    private double _contentHeight;

    /// <summary>是否显示提醒遮罩。</summary>
    public bool IsMaskVisible { get; set; }

    /// <summary>提醒遮罩文本。</summary>
    public string MaskText { get; set; } = "";

    /// <summary>提醒遮罩左图标（Segoe MDL2 字形，空则不画）。</summary>
    public string MaskLeftIcon { get; set; } = "";

    /// <summary>提醒遮罩右图标（Segoe MDL2 字形，空则不画）。</summary>
    public string MaskRightIcon { get; set; } = "";

    /// <summary>是否显示提醒 overlay。</summary>
    public bool IsOverlayVisible { get; set; }

    /// <summary>提醒 overlay 文本。</summary>
    public string OverlayText { get; set; } = "";

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
        var desired = new Size(0, 0);

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

            _lines.Add(new IslandLine(group.Key, components, sizes, height));
            desired.Width = Math.Max(desired.Width, width);
            desired.Height += height;
        }

        _contentHeight = desired.Height;
        return desired;
    }

    public void Render(DrawingContext drawingContext, Rect bounds)
    {
        var settings = _context.Settings;

        // 1) 底色条：独立于内容淡出。提醒 overlay 阶段 ContentOpacity 归 0 时底色条仍需保留。
        var backgroundY = 0.0;
        foreach (var line in _lines)
        {
            var opacity = GetLineOpacity(line.LineNumber);
            var faded = opacity < 0.999;
            if (faded)
                drawingContext.PushOpacity(opacity);

            var lineRect = new Rect(0, backgroundY, bounds.Width, line.Height);
            if (lineRect.Width > 0 && lineRect.Height > 0)
            {
                drawingContext.DrawRoundedRectangle(_backgroundBrush, null, lineRect,
                    settings.RadiusX, settings.RadiusX);
            }

            if (faded)
                drawingContext.Pop();

            backgroundY += line.Height;
        }

        // 2) 内容组件：受 ContentOpacity 控制（提醒时会被压低到 0）。
        var contentOpaque = ContentOpacity < 1;
        if (contentOpaque)
            drawingContext.PushOpacity(ContentOpacity);
        var y = 0.0;
        foreach (var line in _lines)
        {
            var opacity = GetLineOpacity(line.LineNumber);
            var faded = opacity < 0.999;
            if (faded)
                drawingContext.PushOpacity(opacity);

            var x = 0.0;
            for (var i = 0; i < line.Components.Length; i++)
            {
                var slot = new Rect(x, y, line.Sizes[i].Width, line.Height);
                line.Components[i].Render(drawingContext, slot, _context);
                x += slot.Width;
            }

            if (faded)
                drawingContext.Pop();

            y += line.Height;
        }

        if (contentOpaque)
            drawingContext.Pop();

        // 3) overlay 正文。
        if (IsOverlayVisible && !string.IsNullOrEmpty(OverlayText))
        {
            var overlayOpaque = OverlayOpacity < 1;
            if (overlayOpaque)
                drawingContext.PushOpacity(OverlayOpacity);
            var text = MakeText(OverlayText, _context.BodyFontSize, new SolidColorBrush(_context.ForegroundColor),
                FontWeights.Normal);
            drawingContext.DrawText(text, new Point(
                Math.Max(0, (bounds.Width - text.Width) / 2),
                Math.Max(0, (_contentHeight - text.Height) / 2)));
            if (overlayOpaque)
                drawingContext.Pop();
        }

        // 4) 遮罩（底色层 + 内容层）。底色层始终不透明，只做位移/分区展开；内容层单独淡入。
        if (IsMaskVisible)
        {
            drawingContext.PushClip(new RectangleGeometry(new Rect(0, 0, bounds.Width, _contentHeight),
                settings.RadiusX, settings.RadiusX));
            var accent = new SolidColorBrush(_context.AccentColor);
            if (UseSlantedMask)
                DrawSlantedMask(drawingContext, bounds, accent);
            else
                drawingContext.DrawRoundedRectangle(accent, null,
                    new Rect(0, MaskOffsetY, bounds.Width, _contentHeight),
                    settings.RadiusX, settings.RadiusX);
            DrawMaskContent(drawingContext, bounds);
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
                bounds.Width / 2, _contentHeight / 2));
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
        var x = Math.Max(0, (bounds.Width - total) / 2);
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

            var p1 = new Point(center - currentWidth / 2, 0);
            var p2 = new Point(center + currentWidth / 2, 0);
            var p3 = new Point(center + currentWidth / 2 - offset, h);
            var p4 = new Point(center - currentWidth / 2 - offset, h);

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

    /// <summary>命中测试：给定自然坐标，返回所在行号，未命中返回 -1。</summary>
    public int HitTestLine(Point point)
    {
        var y = 0.0;
        foreach (var line in _lines)
        {
            if (point.Y >= y && point.Y <= y + line.Height)
                return line.LineNumber;
            y += line.Height;
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
        public IslandLine(int lineNumber, IIslandComponent[] components, Size[] sizes, double height)
        {
            LineNumber = lineNumber;
            Components = components;
            Sizes = sizes;
            Height = height;
        }

        public int LineNumber { get; }

        public IIslandComponent[] Components { get; }

        public Size[] Sizes { get; }

        public double Height { get; }
    }
}
