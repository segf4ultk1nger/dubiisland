using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using ClassIsland.Core.Models.Components;
using ClassIsland.Models.ComponentSettings;
using ClassIsland.Shared;
using ClassIsland.Shared.Models.Profile;

namespace ClassIsland.Controls.Island.Components;

/// <summary>
/// 课表组件（P4 试点）：直接读课程/档案/时间服务并自绘，不经过任何 XAML。
/// 外观对齐 <c>LessonControlExpanded</c> / <c>LessonControlMinimized</c>。
/// </summary>
public sealed class ScheduleIslandComponent : IslandComponentBase
{
    private const double StripHeight = 40;
    private const double SpacerBase = 16;
    private const double ItemGap = 6;
    private const double PillPadding = 8;
    private const double SeparatorSpan = 10;

    private readonly LessonControlSettings _settings;
    private readonly List<Segment> _segments = new();

    private Brush _foreground = Brushes.White;
    private Brush _accent = Brushes.DodgerBlue;
    private Brush _progressTrack = Brushes.White;
    private readonly Brush _changed = Frozen(Color.FromRgb(0xFF, 0xD5, 0x4F));
    private Pen _separatorPen = new(Brushes.White, 2);
    private Pen _accentPen = new(Brushes.DodgerBlue, 1);
    private Color _lastForeground;
    private Color _lastAccent;

    private string? _placeholderText;

    public ScheduleIslandComponent(ComponentSettings component, LessonControlSettings settings) : base(component)
        => _settings = settings;

    protected override Size MeasureContent(Size availableSize, IslandContext context)
    {
        Build(context);
        if (_placeholderText != null)
            return new Size(200, StripHeight);
        return new Size(_segments.Sum(s => s.Width), StripHeight);
    }

    protected override void RenderContent(DrawingContext drawingContext, Rect slot, IslandContext context)
    {
        if (_placeholderText != null)
        {
            var text = MakeText(_placeholderText, EmphasizedFontSize, FontWeights.Normal, _foreground, context);
            drawingContext.DrawText(text, new Point(slot.X + 12, slot.Y + (slot.Height - text.Height) / 2));
            return;
        }

        var x = slot.X;
        foreach (var segment in _segments)
        {
            var rect = new Rect(x, slot.Y, segment.Width, slot.Height);
            switch (segment.Kind)
            {
                case SegmentKind.Separator:
                    DrawSeparator(drawingContext, rect);
                    break;
                case SegmentKind.Minimized:
                    DrawMinimized(drawingContext, rect, segment, context);
                    break;
                default:
                    DrawExpanded(drawingContext, rect, segment, context);
                    break;
            }

            x += segment.Width;
        }
    }

    private void Build(IslandContext context)
    {
        EnsureBrushes(context);
        _segments.Clear();
        _placeholderText = null;

        var plan = context.LessonsService.CurrentClassPlan;
        if (plan == null)
        {
            _placeholderText = _settings.PlaceholderTextNoClass;
            return;
        }

        var layouts = plan.TimeLayout.Layouts;
        var valid = plan.ValidTimeLayoutItems;
        var subjects = context.ProfileService.Profile.Subjects;
        var now = context.ExactTimeService.GetCurrentLocalDateTime();
        var index = context.LessonsService.CurrentSelectedIndex;
        var selected = index >= 0 && index < layouts.Count ? layouts[index] : null;
        var onClassItems = layouts.Where(t => t.TimeType == 0).ToList();

        foreach (var item in layouts)
        {
            var kind = Decide(item, selected, valid, now);
            if (kind == null)
                continue;

            if (kind == SegmentKind.Separator)
            {
                _segments.Add(new Segment
                {
                    Kind = SegmentKind.Separator,
                    Width = SeparatorSpan * 2 * _settings.ScheduleSpacing
                });
                continue;
            }

            var isBreak = item.TimeType == 1;
            var info = isBreak ? null : GetClassInfo(plan, onClassItems, item);
            var subject = GetSubject(subjects, info);
            var segment = new Segment
            {
                Kind = kind.Value,
                Text = kind == SegmentKind.Expanded
                    ? isBreak ? item.BreakNameText : subject?.Name ?? ""
                    : isBreak ? "休" : subject?.Initial ?? "?",
                Changed = info?.IsChangedClass ?? false,
                Item = item
            };
            segment.Width = MeasureSegment(segment, context);
            _segments.Add(segment);
        }
    }

    private SegmentKind? Decide(TimeLayoutItem item, TimeLayoutItem? selected,
        ICollection<TimeLayoutItem> valid, DateTime now)
    {
        if (item.TimeType == 3)
            return null;
        if (item != selected && selected?.TimeType == 0 && _settings.ShowCurrentLessonOnlyOnClass)
            return null;
        if (!valid.Contains(item))
            return null;

        var itemTime = item.TimeType == 2 ? item.StartSecond : item.EndSecond;
        if (_settings.HideFinishedClass &&
            (itemTime.TimeOfDay < selected?.StartSecond.TimeOfDay || itemTime.TimeOfDay < now.TimeOfDay))
            return null;

        if (item.TimeType == 2)
            return SegmentKind.Separator;

        var hide = (item.TimeType == 1 || item.IsHideDefault) && item != selected;
        if (hide)
            return null;

        return item == selected ? SegmentKind.Expanded : SegmentKind.Minimized;
    }

    private double MeasureSegment(Segment segment, IslandContext context)
    {
        var pad = SpacerBase * _settings.ScheduleSpacing;
        if (segment.Kind == SegmentKind.Minimized)
        {
            var initial = MakeText(segment.Text, EmphasizedFontSize, FontWeights.Bold, _foreground, context);
            return pad * 2 + initial.Width;
        }

        var name = MakeText(segment.Text, EmphasizedFontSize, FontWeights.Bold, _foreground, context);
        var width = pad * 2 + name.Width;

        var extra = GetExtraText(segment.Item!, context, out var pill, out _);
        if (extra != null)
        {
            var font = MakeText(extra, pill ? BodyFontSize : SecondaryFontSize,
                FontWeights.Normal, _foreground, context);
            width += ItemGap + font.Width + (pill ? PillPadding * 2 : 0);
        }

        return width;
    }

    private void DrawSeparator(DrawingContext drawingContext, Rect rect)
    {
        var center = rect.X + rect.Width / 2;
        drawingContext.DrawLine(_separatorPen,
            new Point(center, rect.Y + (rect.Height - 25) / 2),
            new Point(center, rect.Y + (rect.Height + 25) / 2));
    }

    private void DrawMinimized(DrawingContext drawingContext, Rect rect, Segment segment, IslandContext context)
    {
        var pad = SpacerBase * _settings.ScheduleSpacing;
        var text = MakeText(segment.Text, EmphasizedFontSize, FontWeights.Bold, _foreground, context);
        var x = rect.X + pad;

        if (segment.Changed && _settings.HighlightChangedClass)
        {
            drawingContext.DrawRoundedRectangle(_changed, null,
                new Rect(x - 3, rect.Y + 5, text.Width + 6, rect.Height - 10), 4, 4);
        }

        drawingContext.DrawText(text, new Point(x, rect.Y + (rect.Height - text.Height) / 2));
    }

    private void DrawExpanded(DrawingContext drawingContext, Rect rect, Segment segment, IslandContext context)
    {
        var pad = SpacerBase * _settings.ScheduleSpacing;
        var contentX = rect.X + pad;

        var name = MakeText(segment.Text, EmphasizedFontSize, FontWeights.Bold, _foreground, context);
        if (segment.Changed && _settings.HighlightChangedClass)
        {
            drawingContext.DrawRoundedRectangle(_changed, null,
                new Rect(contentX - 3, rect.Y + 5, name.Width + 6, rect.Height - 10), 4, 4);
        }

        var nameY = rect.Y + (rect.Height - name.Height) / 2;
        drawingContext.DrawText(name, new Point(contentX, nameY));

        var extra = GetExtraText(segment.Item!, context, out var pill, out _);
        if (extra != null)
        {
            var extraX = contentX + name.Width + ItemGap;
            if (pill)
            {
                DrawCountdownPill(drawingContext, rect, extraX, extra, context);
            }
            else
            {
                var font = MakeText(extra, SecondaryFontSize, FontWeights.Normal, _foreground, context);
                drawingContext.DrawText(font, new Point(extraX, rect.Y + rect.Height - font.Height - 4));
            }
        }

        var item = segment.Item!;
        var total = (long)item.Last.TotalSeconds;
        if (total > 0)
        {
            var elapsed = (long)(context.ExactTimeService.GetCurrentLocalDateTime().TimeOfDay - item.StartSecond.TimeOfDay).TotalSeconds;
            var fraction = Math.Max(0, Math.Min(1, (double)elapsed / total));
            var track = new Rect(rect.X, rect.Y + rect.Height - 3, rect.Width, 3);
            drawingContext.DrawRectangle(_progressTrack, null, track);
            drawingContext.DrawRectangle(_accent, null, new Rect(track.X, track.Y, track.Width * fraction, track.Height));
        }
    }

    private void DrawCountdownPill(DrawingContext drawingContext, Rect rect, double x, string text, IslandContext context)
    {
        var font = MakeText(text, BodyFontSize, FontWeights.Normal, _foreground, context);
        var width = font.Width + PillPadding * 2;
        var height = Math.Min(rect.Height - 8, font.Height + 4);
        var pill = new Rect(x, rect.Y + rect.Height - height - 2, width, height);
        drawingContext.DrawRoundedRectangle(null, _accentPen, pill, height / 2, height / 2);
        drawingContext.DrawText(font, new Point(pill.X + PillPadding, pill.Y + (height - font.Height) / 2));
    }

    private string? GetExtraText(TimeLayoutItem item, IslandContext context, out bool pill, out long leftSeconds)
    {
        pill = false;
        var total = (long)item.Last.TotalSeconds;
        var elapsed = (long)(context.ExactTimeService.GetCurrentLocalDateTime().TimeOfDay - item.StartSecond.TimeOfDay).TotalSeconds;
        leftSeconds = total - elapsed;

        if (leftSeconds <= _settings.CountdownSeconds && _settings.IsCountdownEnabled)
        {
            pill = true;
            return _settings.IsNonExactCountdownEnabled
                ? $"< {FormatSeconds(_settings.CountdownSeconds, false, true)}"
                : $"-{FormatSeconds(leftSeconds, true, false)}";
        }

        if (!_settings.ShowExtraInfoOnTimePoint)
            return null;

        return _settings.ExtraInfoType switch
        {
            0 => $"{item.StartSecond:HH:mm}-{item.EndSecond:HH:mm}",
            1 => FormatMulti(elapsed, total, false),
            2 => "-" + FormatMulti(leftSeconds, total, true),
            3 => total > 0 ? $"{(double)elapsed / total:P0}" : "0%",
            4 => "-" + FormatSeconds(leftSeconds, false, true),
            5 => "-" + FormatSeconds(leftSeconds, true, false),
            _ => ""
        };
    }

    private void EnsureBrushes(IslandContext context)
    {
        if (_lastForeground != EffectiveForegroundColor)
        {
            _lastForeground = EffectiveForegroundColor;
            _foreground = Frozen(EffectiveForegroundColor);
            var separatorBrush = new SolidColorBrush(EffectiveForegroundColor) { Opacity = 0.8 };
            separatorBrush.Freeze();
            _separatorPen = new Pen(separatorBrush, 2);
            _separatorPen.Freeze();

            var track = new SolidColorBrush(EffectiveForegroundColor) { Opacity = 0.25 };
            track.Freeze();
            _progressTrack = track;
        }

        if (_lastAccent != context.AccentColor)
        {
            _lastAccent = context.AccentColor;
            _accent = Frozen(context.AccentColor);
            _accentPen = new Pen(_accent, 1);
            _accentPen.Freeze();
        }
    }

    private static ClassInfo? GetClassInfo(ClassPlan plan, List<TimeLayoutItem> onClassItems, TimeLayoutItem item)
    {
        var index = onClassItems.IndexOf(item);
        return index >= 0 && index < plan.Classes.Count ? plan.Classes[index] : null;
    }

    private static Subject? GetSubject(ObservableDictionary<string, Subject> subjects, ClassInfo? info)
    {
        if (info == null || string.IsNullOrEmpty(info.SubjectId))
            return null;
        return subjects.TryGetValue(info.SubjectId, out var subject) ? subject : null;
    }

    private static FormattedText MakeText(string text, double size, FontWeight weight, Brush brush, IslandContext context)
        => new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(context.FontFamily, FontStyles.Normal, weight, FontStretches.Normal),
            size, brush, context.PixelsPerDip);

    private static string FormatSeconds(long seconds, bool withSeconds, bool ceiling)
    {
        var value = TimeSpan.FromSeconds(seconds);
        if (withSeconds)
        {
            if (value.TotalSeconds >= 3600)
                return $"{Math.Floor(value.TotalHours)}:{value.Minutes:00}:{value.Seconds:00}";
            if (value.TotalSeconds >= 60)
                return $"{value.Minutes}:{value.Seconds:00}";
            return value.TotalSeconds >= 0 ? $"{value.Seconds}s" : "";
        }

        var rounded = TimeSpan.FromMinutes(ceiling ? Math.Ceiling(value.TotalMinutes) : Math.Floor(value.TotalMinutes));
        if (rounded.TotalSeconds >= 3600)
            return $"{Math.Floor(rounded.TotalHours)}h{rounded.Minutes:00}m";
        return rounded.TotalSeconds >= 0 ? $"{rounded.Minutes}min" : "";
    }

    private static string FormatMulti(long seconds, long total, bool ceiling)
    {
        var elapsed = TimeSpan.FromMinutes(ceiling ? Math.Ceiling(seconds / 60.0) : Math.Floor(seconds / 60.0));
        var whole = TimeSpan.FromMinutes(Math.Round(total / 60.0));
        return $"{Part(elapsed)}/{Part(whole)}";
    }

    private static string Part(TimeSpan value)
        => value.TotalHours >= 1 ? $"{Math.Floor(value.TotalHours)}h{value.Minutes:00}m" : $"{value.Minutes}m";

    private enum SegmentKind
    {
        Minimized,
        Expanded,
        Separator
    }

    private sealed class Segment
    {
        public SegmentKind Kind;
        public double Width;
        public string Text = "";
        public bool Changed;
        public TimeLayoutItem? Item;
    }
}
