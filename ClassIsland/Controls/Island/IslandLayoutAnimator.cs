using System;
using System.Collections.Generic;
using System.Windows.Media.Animation;
using ClassIsland.Core.Models.Components;

namespace ClassIsland.Controls.Island;

/// <summary>
/// 岛各行几何（宽/顶/高）的补间引擎。每次 <see cref="Feed"/> 一批目标值，逐帧 <see cref="Tick"/> 推进，
/// 补间期间窗口按 <see cref="ReservedWidth"/>/<see cref="ReservedHeight"/> 预留放大，静止后归零。
/// </summary>
public sealed class IslandLayoutAnimator
{
    private const double GrowDuration = 0.6;
    private const double ShrinkDuration = 0.8;
    private const double ReserveFactor = 1.15;
    private const double Epsilon = 0.0001;
    private const double ChangeThreshold = 0.01;
    private const double MaxJellySquash = 0.08;
    private const double JellyCoupling = 0.5;
    private const double ComponentMoveDuration = 0.3;
    private const double ComponentEnterDuration = 0.22;
    private const double ComponentScaleFrom = 0.9;
    private const double ComponentEnterStagger = 0.03; // 多个新组件依次入场的错峰间隔
    private const double DockDuration = 0.36;
    private const double MotionStretchAmount = 0.07; // 停靠位移时沿运动方向的最大拉伸比例（丰富档）

    // 缓动函数无状态，可共享复用，避免每次目标变化都分配。
    private static readonly BackEase GrowEase = new() { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 };
    private static readonly BackEase ShrinkEase = new() { EasingMode = EasingMode.EaseOut, Amplitude = 0.2 };
    private static readonly CubicEase MoveEase = new() { EasingMode = EasingMode.EaseOut };
    private static readonly CubicEase EnterEase = new() { EasingMode = EasingMode.EaseOut };
    private static readonly BackEase DockEase = new() { EasingMode = EasingMode.EaseOut, Amplitude = 0.1 };

    private readonly Dictionary<int, LineState> _lines = new();
    private readonly HashSet<int> _presentLines = new();
    private readonly List<int> _removedLines = new();
    private readonly Dictionary<ComponentSettings, ComponentState> _components = new();
    private readonly HashSet<ComponentSettings> _presentComponents = new();
    private readonly List<ComponentSettings> _removedComponents = new();
    private bool _motionPrimed;

    private readonly Channel _contentWidth = new();
    private bool _contentWidthPrimed;

    private readonly Channel _dockH = new();
    private readonly Channel _dockV = new();
    private bool _dockPrimed;

    /// <summary>是否启用几何补间（精简档关闭）。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>是否启用纵向果冻形变（仅丰富档开启）。</summary>
    public bool Jelly { get; set; }

    /// <summary>任一通道 Current 与 Target 不一致时为 true。</summary>
    public bool IsAnimating { get; private set; }

    /// <summary>过渡期窗口预留宽度（内容自然坐标 DIP，含外边距），静止/关闭时为 0。</summary>
    public double ReservedWidth { get; private set; }

    /// <summary>过渡期窗口预留高度，静止/关闭时为 0。</summary>
    public double ReservedHeight { get; private set; }

    /// <summary>批量送入各行目标几何。对同一批目标重复调用是幂等的，不会重启补间。</summary>
    public void Feed(IReadOnlyList<(int Line, double Width, double Top, double Height)> targets)
    {
        _presentLines.Clear();
        for (var i = 0; i < targets.Count; i++)
        {
            var (line, width, top, height) = targets[i];
            _presentLines.Add(line);
            if (!_lines.TryGetValue(line, out var state))
            {
                _lines[line] = new LineState
                {
                    Width = new Channel { Current = width, From = width, Target = width },
                    Top = new Channel { Current = top, From = top, Target = top },
                    Height = new Channel { Current = height, From = height, Target = height }
                };
                continue;
            }

            if (!Enabled)
            {
                SetDirect(state.Width, width);
                SetDirect(state.Top, top);
                SetDirect(state.Height, height);
                continue;
            }

            UpdateChannel(state.Width, width);
            UpdateChannel(state.Top, top);
            UpdateChannel(state.Height, height);
        }

        _removedLines.Clear();
        foreach (var line in _lines.Keys)
        {
            if (!_presentLines.Contains(line))
                _removedLines.Add(line);
        }

        foreach (var line in _removedLines)
            _lines.Remove(line);

        RecomputeReserved();
    }

    /// <summary>批量送入组件目标 X（行内相对坐标，不含整行停靠对齐偏移）。对同一批重复调用幂等。</summary>
    public void FeedComponents(IReadOnlyList<(ComponentSettings Key, double X)> targets)
    {
        _presentComponents.Clear();
        var enterIndex = 0;
        for (var i = 0; i < targets.Count; i++)
        {
            var (key, x) = targets[i];
            _presentComponents.Add(key);
            if (!_components.TryGetValue(key, out var state))
            {
                state = new ComponentState { X = new Channel { Current = x, From = x, Target = x } };
                if (_motionPrimed && Enabled)
                {
                    // 逐个错峰：同一批同时入场的新组件依次延后，单独新增则不延迟。
                    var delay = enterIndex++ * ComponentEnterStagger;
                    state.Opacity = new Channel
                    {
                        Current = 0, From = 0, Target = 1,
                        StartSeconds = -1, Delay = delay, Duration = ComponentEnterDuration, Easing = EnterEase
                    };
                    state.Scale = new Channel
                    {
                        Current = ComponentScaleFrom, From = ComponentScaleFrom, Target = 1,
                        StartSeconds = -1, Delay = delay, Duration = ComponentEnterDuration, Easing = EnterEase
                    };
                }
                else
                {
                    state.Opacity = new Channel { Current = 1, From = 1, Target = 1 };
                    state.Scale = new Channel { Current = 1, From = 1, Target = 1 };
                }

                _components[key] = state;
                continue;
            }

            if (!Enabled)
            {
                SetDirect(state.X, x);
                continue;
            }

            SetTarget(state.X, x, ComponentMoveDuration, MoveEase);
        }

        _removedComponents.Clear();
        foreach (var key in _components.Keys)
        {
            if (!_presentComponents.Contains(key))
                _removedComponents.Add(key);
        }

        foreach (var key in _removedComponents)
            _components.Remove(key);

        _motionPrimed = true;
        RecomputeReserved();
    }

    /// <summary>送入岛内容目标宽度（含提醒遮罩/overlay 撑大的部分）。首帧直接落位。</summary>
    public void FeedContentWidth(double width)
    {
        if (!_contentWidthPrimed)
        {
            _contentWidth.Current = _contentWidth.From = _contentWidth.Target = width;
            _contentWidthPrimed = true;
        }
        else if (!Enabled)
        {
            SetDirect(_contentWidth, width);
        }
        else
        {
            UpdateChannel(_contentWidth, width);
        }

        RecomputeReserved();
    }

    /// <summary>取补间中的内容宽度；未启用或未初始化时返回 fallback。</summary>
    public double GetContentWidth(double fallback)
        => Enabled && _contentWidthPrimed ? _contentWidth.Current : fallback;

    /// <summary>送入停靠对齐目标（0/0.5/1）；停靠位置变化时缓动，否则保持。</summary>
    public void FeedDockAlign(double hAlign, double vAlign)
    {
        if (!_dockPrimed)
        {
            _dockH.Current = _dockH.From = _dockH.Target = hAlign;
            _dockV.Current = _dockV.From = _dockV.Target = vAlign;
            _dockPrimed = true;
        }
        else if (!Enabled)
        {
            SetDirect(_dockH, hAlign);
            SetDirect(_dockV, vAlign);
        }
        else
        {
            SetTarget(_dockH, hAlign, DockDuration, DockEase);
            SetTarget(_dockV, vAlign, DockDuration, DockEase);
        }

        RecomputeReserved();
    }

    /// <summary>停靠对齐补间中（窗口需逐帧重定位）。</summary>
    public bool IsDockAnimating => _dockPrimed && (_dockH.NeedsTick || _dockV.NeedsTick);

    public double GetDockHAlign(double fallback) => Enabled && _dockPrimed ? _dockH.Current : fallback;

    public double GetDockVAlign(double fallback) => Enabled && _dockPrimed ? _dockV.Current : fallback;

    /// <summary>推进补间，返回是否仍在动。</summary>
    public bool Tick(double nowSeconds)
    {
        if (!Enabled)
            return false;

        var animating = false;
        foreach (var state in _lines.Values)
        {
            animating |= Step(state.Width, nowSeconds);
            animating |= Step(state.Top, nowSeconds);
            animating |= Step(state.Height, nowSeconds);
        }

        foreach (var state in _components.Values)
        {
            animating |= Step(state.X, nowSeconds);
            animating |= Step(state.Opacity, nowSeconds);
            animating |= Step(state.Scale, nowSeconds);
        }

        if (_contentWidthPrimed)
            animating |= Step(_contentWidth, nowSeconds);

        if (_dockPrimed)
        {
            animating |= Step(_dockH, nowSeconds);
            animating |= Step(_dockV, nowSeconds);
        }

        RecomputeReserved();
        return animating;
    }

    /// <summary>取该行补间中的宽度；未启用或无该行时返回 fallback。</summary>
    public double GetWidth(int line, double fallback)
        => Enabled && _lines.TryGetValue(line, out var state) ? state.Width.Current : fallback;

    /// <summary>取该行补间中的顶部 Y；未启用或无该行时返回 fallback。</summary>
    public double GetTop(int line, double fallback)
        => Enabled && _lines.TryGetValue(line, out var state) ? state.Top.Current : fallback;

    /// <summary>取该行补间中的高度；未启用或无该行时返回 fallback。</summary>
    public double GetHeight(int line, double fallback)
        => Enabled && _lines.TryGetValue(line, out var state) ? state.Height.Current : fallback;

    /// <summary>取组件补间中的 X；未启用或无该组件时返回 fallback。</summary>
    public double GetComponentX(ComponentSettings key, double fallback)
        => Enabled && _components.TryGetValue(key, out var state) ? state.X.Current : fallback;

    /// <summary>取组件补间中的不透明度；未启用或无该组件时为 1。</summary>
    public double GetComponentOpacity(ComponentSettings key)
        => Enabled && _components.TryGetValue(key, out var state) ? state.Opacity.Current : 1.0;

    /// <summary>取组件补间中的缩放；未启用或无该组件时为 1。</summary>
    public double GetComponentScale(ComponentSettings key)
        => Enabled && _components.TryGetValue(key, out var state) ? state.Scale.Current : 1.0;

    /// <summary>该行当前的纵向形变缩放（果冻，1=无形变）。未启用/非果冻/无该行时为 1。</summary>
    public double GetJellyScaleY(int line)
    {
        if (!Enabled || !Jelly || !_lines.TryGetValue(line, out var state))
            return 1.0;
        return 1.0 + SquashOf(state);
    }

    private static double SquashOf(LineState state)
    {
        var w = state.Width;
        var span = w.Target - w.From;
        if (Math.Abs(span) < ChangeThreshold)
            return 0;
        var progress = (w.Current - w.From) / span;
        if (progress <= 0 || progress >= 1)
            return 0; // 只在补间进行中；结束时 progress==1 -> 0
        var bump = Math.Sin(Math.PI * progress); // 中段峰值，两端为 0
        var amount = Math.Min(MaxJellySquash,
            Math.Abs(span) / Math.Max(1.0, Math.Max(w.From, w.Target)) * JellyCoupling);
        return -Math.Sign(span) * amount * bump; // 变宽 -> 纵向压扁；变窄 -> 纵向拉长
    }

    /// <summary>
    ///     停靠位移期间的整体挤压拉伸量（正=沿水平方向拉伸、竖直压扁；负=反之），供窗口内已预留的透明余量内做形变。
    ///     未启用/非丰富档/无位移时为 0。
    /// </summary>
    public double GetMotionStretch()
    {
        if (!Enabled || !Jelly || !_dockPrimed)
            return 0;
        return DockBump(_dockH) - DockBump(_dockV);
    }

    private static double DockBump(Channel c)
    {
        var span = c.Target - c.From;
        if (Math.Abs(span) < ChangeThreshold)
            return 0;
        var progress = (c.Current - c.From) / span;
        if (progress <= 0 || progress >= 1)
            return 0; // 只在补间进行中；结束时归零
        return MotionStretchAmount * Math.Sin(2 * Math.PI * progress); // 前段拉伸、后段回弹
    }

    /// <summary>全部对齐目标并清零预留。</summary>
    public void Settle()
    {
        foreach (var state in _lines.Values)
        {
            SettleChannel(state.Width);
            SettleChannel(state.Top);
            SettleChannel(state.Height);
        }

        foreach (var state in _components.Values)
        {
            SettleChannel(state.X);
            SettleChannel(state.Opacity);
            SettleChannel(state.Scale);
        }

        SettleChannel(_contentWidth);
        SettleChannel(_dockH);
        SettleChannel(_dockV);

        IsAnimating = false;
        ReservedWidth = 0;
        ReservedHeight = 0;
    }

    private static void UpdateChannel(Channel channel, double value)
    {
        var grow = value > channel.Current;
        SetTarget(channel, value, grow ? GrowDuration : ShrinkDuration, grow ? GrowEase : ShrinkEase);
    }

    private static void SetTarget(Channel channel, double value, double duration, IEasingFunction easing)
    {
        if (Math.Abs(channel.Target - value) < ChangeThreshold)
            return;

        channel.From = channel.Current;
        channel.Target = value;
        channel.StartSeconds = -1;
        channel.Delay = 0;
        channel.Duration = duration;
        channel.Easing = easing;
    }

    private static void SetDirect(Channel channel, double value)
    {
        channel.Current = value;
        channel.From = value;
        channel.Target = value;
        channel.StartSeconds = -1;
        channel.Duration = 0;
        channel.Easing = null;
    }

    private static bool Step(Channel channel, double nowSeconds)
    {
        if (!channel.NeedsTick)
        {
            channel.Current = channel.Target;
            channel.From = channel.Target;
            return false;
        }

        if (channel.StartSeconds < 0)
            channel.StartSeconds = nowSeconds;
        var t = channel.Duration <= 0
            ? 1
            : (nowSeconds - channel.StartSeconds - channel.Delay) / channel.Duration;
        if (t <= 0)
        {
            channel.Current = channel.From; // 错峰延迟期内停在起点
            return true;
        }
        if (t >= 1)
        {
            channel.Current = channel.Target;
            channel.From = channel.Target;
            return false;
        }

        var eased = channel.Easing?.Ease(t) ?? t;
        channel.Current = channel.From + (channel.Target - channel.From) * eased;
        return true;
    }

    private static void SettleChannel(Channel channel)
    {
        channel.Current = channel.Target;
        channel.From = channel.Target;
        channel.StartSeconds = -1;
        channel.Delay = 0;
    }

    private void RecomputeReserved()
    {
        IsAnimating = false;
        foreach (var state in _lines.Values)
        {
            if (state.Width.NeedsTick || state.Top.NeedsTick || state.Height.NeedsTick)
            {
                IsAnimating = true;
                break;
            }
        }

        if (!IsAnimating)
        {
            foreach (var state in _components.Values)
            {
                if (state.X.NeedsTick || state.Opacity.NeedsTick || state.Scale.NeedsTick)
                {
                    IsAnimating = true;
                    break;
                }
            }
        }

        if (!IsAnimating && _contentWidthPrimed && _contentWidth.NeedsTick)
            IsAnimating = true;

        // 预留只在几何/宽度补间时需要；停靠对齐补间仅移动窗口，不预留，故单独记录。
        var reserveActive = IsAnimating;

        if (!IsAnimating && _dockPrimed && (_dockH.NeedsTick || _dockV.NeedsTick))
            IsAnimating = true;

        if (!Enabled || !reserveActive)
        {
            ReservedWidth = 0;
            ReservedHeight = 0;
            return;
        }

        var maxWidth = 0.0;
        var maxHeight = 0.0;
        foreach (var state in _lines.Values)
        {
            maxWidth = Math.Max(maxWidth, Math.Max(state.Width.From, state.Width.Target));
            maxHeight = Math.Max(maxHeight,
                Math.Max(state.Top.From + state.Height.From, state.Top.Target + state.Height.Target));
        }

        var contentMax = _contentWidthPrimed ? Math.Max(_contentWidth.From, _contentWidth.Target) : 0;

        ReservedWidth = ReserveFactor * Math.Max(maxWidth, contentMax);
        ReservedHeight = ReserveFactor * maxHeight;
    }

    private sealed class Channel
    {
        public double Current;
        public double From;
        public double Target;
        public double StartSeconds = -1;
        public double Delay;
        public double Duration;
        public IEasingFunction? Easing;
        public bool NeedsTick => Math.Abs(Current - Target) > Epsilon;
    }

    private sealed class LineState
    {
        public Channel Width = new();
        public Channel Top = new();
        public Channel Height = new();
    }

    private sealed class ComponentState
    {
        public Channel X = new();
        public Channel Opacity = new();
        public Channel Scale = new();
    }
}
