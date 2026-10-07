using System;
using System.Collections.Generic;
using System.Windows.Media.Animation;

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

    // 缓动函数无状态，可共享复用，避免每次目标变化都分配。
    private static readonly BackEase GrowEase = new() { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 };
    private static readonly BackEase ShrinkEase = new() { EasingMode = EasingMode.EaseOut, Amplitude = 0.2 };

    private readonly Dictionary<int, LineState> _lines = new();
    private readonly HashSet<int> _presentLines = new();
    private readonly List<int> _removedLines = new();

    /// <summary>是否启用几何补间（精简档关闭）。</summary>
    public bool Enabled { get; set; } = true;

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

    /// <summary>全部对齐目标并清零预留。</summary>
    public void Settle()
    {
        foreach (var state in _lines.Values)
        {
            SettleChannel(state.Width);
            SettleChannel(state.Top);
            SettleChannel(state.Height);
        }

        IsAnimating = false;
        ReservedWidth = 0;
        ReservedHeight = 0;
    }

    private static void UpdateChannel(Channel channel, double value)
    {
        if (Math.Abs(channel.Target - value) < ChangeThreshold)
            return;

        var grow = value > channel.Current;
        channel.From = channel.Current;
        channel.Target = value;
        channel.StartSeconds = -1;
        channel.Duration = grow ? GrowDuration : ShrinkDuration;
        channel.Easing = grow ? GrowEase : ShrinkEase;
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
        var t = channel.Duration <= 0 ? 1 : (nowSeconds - channel.StartSeconds) / channel.Duration;
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

        if (!Enabled || !IsAnimating)
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

        ReservedWidth = ReserveFactor * maxWidth;
        ReservedHeight = ReserveFactor * maxHeight;
    }

    private sealed class Channel
    {
        public double Current;
        public double From;
        public double Target;
        public double StartSeconds = -1;
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
}
