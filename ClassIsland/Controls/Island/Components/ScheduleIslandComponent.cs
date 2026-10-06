using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Models.AttachedSettings;
using ClassIsland.Core.Models.Components;
using ClassIsland.Models.ComponentSettings;
using ClassIsland.Shared;
using ClassIsland.Shared.Abstraction.Models;
using ClassIsland.Shared.Enums;
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
    private const double MiniSpacerBase = 10;
    private const double ItemGap = 6;
    private const double PillPadding = 8;
    private const double SeparatorSpan = 10;
    private const double BadgePaddingX = 8;
    private const double BadgePaddingY = 2;
    private const double BadgeGap = 2;
    private const double BadgeCornerRadius = 8;

    private static readonly Guid LessonControlAttachedSettingsId = new("58e5b69a-764a-472b-bcf7-003b6a8c7fdf");

    private readonly LessonControlSettings _settings;
    private readonly List<Segment> _segments = new();

    private Brush _foreground = Brushes.White;
    private Brush _accent = Brushes.DodgerBlue;
    private Brush _accentFill = Brushes.DodgerBlue;
    private Brush _progressTrack = Brushes.White;
    private static readonly Brush ChangedGlow = CreateChangedGlow();
    private Pen _separatorPen = new(Brushes.White, 2);
    private Pen _accentPen = new(Brushes.DodgerBlue, 1);
    private Color _lastForeground;
    private Color _lastAccent;

    private string? _placeholderText;
    private bool _collapsed;
    private bool _liveUpdating;

    private bool _showTomorrowBadge;
    private FormattedText? _badgeText;
    private double _badgeWidth;
    private double _badgeHeight;
    private Brush? _badgeForeground;

    public ScheduleIslandComponent(ComponentSettings component, LessonControlSettings settings) : base(component)
        => _settings = settings;

    protected override Size MeasureContent(Size availableSize, IslandContext context)
    {
        Build(context);
        if (_collapsed)
            return new Size(0, 0);
        if (_placeholderText != null)
        {
            var text = MakeText(_placeholderText, BodyFontSize, FontWeights.Normal, _foreground, context);
            return new Size(text.Width + 24, StripHeight);
        }

        var width = _segments.Sum(s => s.Width);
        if (_showTomorrowBadge)
            width += _badgeWidth + BadgeGap;
        return new Size(width, StripHeight);
    }

    private void Build(IslandContext context)
    {
        EnsureBrushes(context);
        _segments.Clear();
        _placeholderText = null;
        _collapsed = false;
        _showTomorrowBadge = false;

        var lessons = context.LessonsService;
        var now = context.ExactTimeService.GetCurrentLocalDateTime();
        var mode = _settings.TomorrowScheduleShowMode;
        var currentPlan = lessons.CurrentClassPlan;
        var isAfterSchool = lessons.CurrentState == TimeState.AfterSchool || currentPlan == null;
        var tomorrowClassPlan = lessons.GetClassPlanByDate(now + TimeSpan.FromDays(1));
        var hideFinishedClass = _settings.HideFinishedClass;
        var badgeVisible = tomorrowClassPlan != null && mode != 0 && !(!isAfterSchool && mode == 1);

        var placeholderVisible = _settings.ShowPlaceholderOnEmptyClassPlan &&
            ((tomorrowClassPlan == null && mode == 2) ||
             (mode == 1 && currentPlan == null && !badgeVisible) ||
             (currentPlan == null && mode == 0) ||
             (isAfterSchool && hideFinishedClass && mode == 0) ||
             (isAfterSchool && hideFinishedClass && mode == 1 && tomorrowClassPlan == null));

        if (placeholderVisible)
        {
            _placeholderText = hideFinishedClass && mode == 0 ||
                               (hideFinishedClass && mode == 1 && tomorrowClassPlan == null)
                ? _settings.PlaceholderTextAllClassEnded
                : _settings.PlaceholderTextNoClass;
            return;
        }

        if (isAfterSchool && hideFinishedClass &&
            (mode == 0 || (mode == 1 && tomorrowClassPlan == null)))
        {
            _collapsed = true;
            return;
        }

        SetTomorrowBadge(context, badgeVisible);

        ClassPlan? plan;
        int index;
        bool hideFinished;
        if (mode == 2)
        {
            plan = tomorrowClassPlan;
            index = -1;
            hideFinished = false;
            _liveUpdating = false;
        }
        else if (tomorrowClassPlan == null || mode == 0)
        {
            plan = lessons.CurrentClassPlan;
            index = lessons.CurrentSelectedIndex;
            hideFinished = _settings.HideFinishedClass;
            _liveUpdating = true;
        }
        else if (!isAfterSchool)
        {
            plan = lessons.CurrentClassPlan;
            index = lessons.CurrentSelectedIndex;
            hideFinished = _settings.HideFinishedClass;
            _liveUpdating = true;
        }
        else
        {
            plan = tomorrowClassPlan;
            index = -1;
            hideFinished = false;
            _liveUpdating = false;
        }

        if (plan == null)
            return;

        var layouts = plan.TimeLayout.Layouts;
        var valid = plan.ValidTimeLayoutItems;
        var subjects = context.ProfileService.Profile.Subjects;
        var selected = index >= 0 && index < layouts.Count ? layouts[index] : null;
        var onClassItems = layouts.Where(t => t.TimeType == 0).ToList();
        var showCurrentLessonOnlyOnClass = ResolveShowCurrentLessonOnlyOnClass();

        foreach (var item in layouts)
        {
            var kind = Decide(item, selected, valid, now, showCurrentLessonOnlyOnClass, hideFinished);
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
                    : subject?.Initial ?? "",
                Changed = info?.IsChangedClass ?? false,
                Item = item,
                Settings = kind == SegmentKind.Expanded
                    ? ResolveAttachedSettings(subject ?? Subject.Breaking, item, plan)
                    : _settings
            };
            segment.Width = MeasureSegment(segment, context);
            _segments.Add(segment);
        }
    }

    private void SetTomorrowBadge(IslandContext context, bool visible)
    {
        if (!visible)
        {
            _badgeText = null;
            _showTomorrowBadge = false;
            return;
        }

        var foreground = _badgeForeground ??= ResolveBadgeForeground();
        _badgeText = MakeText("明天", BodyFontSize, FontWeights.Normal, foreground, context);
        _badgeWidth = _badgeText.Width + BadgePaddingX * 2;
        _badgeHeight = _badgeText.Height + BadgePaddingY * 2;
        _showTomorrowBadge = true;
    }

    private static Brush ResolveBadgeForeground()
        => Application.Current?.TryFindResource("MahApps.Brushes.IdealForeground") as SolidColorBrush ?? Brushes.White;

    protected override void RenderContent(DrawingContext drawingContext, Rect slot, IslandContext context)
    {
        if (_placeholderText != null)
        {
            var text = MakeText(_placeholderText, BodyFontSize, FontWeights.Normal, _foreground, context);
            drawingContext.DrawText(text, new Point(slot.X + 12, slot.Y + (slot.Height - text.Height) / 2));
            return;
        }

        var x = slot.X;
        if (_showTomorrowBadge && _badgeText != null)
        {
            var badgeRect = new Rect(x, slot.Y + (slot.Height - _badgeHeight) / 2, _badgeWidth, _badgeHeight);
            drawingContext.DrawRoundedRectangle(_accent, null, badgeRect, BadgeCornerRadius, BadgeCornerRadius);
            drawingContext.DrawText(_badgeText,
                new Point(badgeRect.X + BadgePaddingX, badgeRect.Y + (_badgeHeight - _badgeText.Height) / 2));
            x += _badgeWidth + BadgeGap;
        }

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

    private ILessonControlSettings ResolveAttachedSettings(Subject subject, TimeLayoutItem item, ClassPlan? classPlan)
        => (ILessonControlSettings?)IAttachedSettingsHostService
               .GetAttachedSettingsByPriority<LessonControlAttachedSettings>(
                   LessonControlAttachedSettingsId, subject, item, classPlan, classPlan?.TimeLayout) ??
           _settings;

    private bool ResolveShowCurrentLessonOnlyOnClass()
    {
        var lessons = Context.LessonsService;
        return ((ILessonControlSettings?)IAttachedSettingsHostService
                    .GetAttachedSettingsByPriority<LessonControlAttachedSettings>(
                        LessonControlAttachedSettingsId,
                        lessons.CurrentSubject,
                        lessons.CurrentTimeLayoutItem,
                        lessons.CurrentClassPlan,
                        lessons.CurrentClassPlan?.TimeLayout) ??
                _settings).ShowCurrentLessonOnlyOnClass;
    }

    private SegmentKind? Decide(TimeLayoutItem item, TimeLayoutItem? selected,
        ICollection<TimeLayoutItem> valid, DateTime now, bool showCurrentLessonOnlyOnClass, bool hideFinished)
    {
        if (item.TimeType == 3)
            return null;
        if (item != selected && selected?.TimeType == 0 && showCurrentLessonOnlyOnClass)
            return null;
        if (!valid.Contains(item))
            return null;

        var itemTime = item.TimeType == 2 ? item.StartTime : item.EndTime;
        if (hideFinished &&
            (itemTime < selected?.StartTime || itemTime < now.TimeOfDay))
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
            var miniPad = MiniSpacerBase * _settings.ScheduleSpacing;
            var initial = MakeText(segment.Text, EmphasizedFontSize, FontWeights.Bold, _foreground, context);
            return miniPad * 2 + initial.Width;
        }

        var name = MakeText(segment.Text, EmphasizedFontSize, FontWeights.Bold, _foreground, context);
        var width = pad * 2 + name.Width;

        var extra = GetExtraText(segment.Item!, segment.Settings, context, out var pill, out _);
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
        var pad = MiniSpacerBase * _settings.ScheduleSpacing;
        var text = MakeText(segment.Text, EmphasizedFontSize, FontWeights.Bold, _foreground, context);
        var origin = new Point(rect.X + pad, rect.Y + (rect.Height - text.Height) / 2);

        if (segment.Changed && _settings.HighlightChangedClass)
            DrawChangedGlow(drawingContext, text, origin, 3);

        drawingContext.DrawText(text, origin);
    }

    private void DrawExpanded(DrawingContext drawingContext, Rect rect, Segment segment, IslandContext context)
    {
        var pad = SpacerBase * _settings.ScheduleSpacing;
        var contentX = rect.X + pad;

        var name = MakeText(segment.Text, EmphasizedFontSize, FontWeights.Bold, _foreground, context);
        var nameOrigin = new Point(contentX, rect.Y + (rect.Height - name.Height) / 2);
        if (segment.Changed && _settings.HighlightChangedClass)
            DrawChangedGlow(drawingContext, name, nameOrigin, 2);

        drawingContext.DrawText(name, nameOrigin);

        var extra = GetExtraText(segment.Item!, segment.Settings, context, out var pill, out _);
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
            var elapsed = (long)(context.ExactTimeService.GetCurrentLocalDateTime().TimeOfDay - item.StartTime).TotalSeconds;
            var fraction = Math.Max(0, Math.Min(1, (double)elapsed / total));
            var track = new Rect(rect.X, rect.Y + rect.Height - 4, rect.Width, 4);
            drawingContext.DrawRoundedRectangle(_progressTrack, null, track, 2, 2);
            drawingContext.DrawRoundedRectangle(_accent, null,
                new Rect(track.X, track.Y, track.Width * fraction, track.Height), 2, 2);
        }
    }

    private void DrawCountdownPill(DrawingContext drawingContext, Rect rect, double x, string text, IslandContext context)
    {
        var font = MakeText(text, BodyFontSize, FontWeights.Normal, _foreground, context);
        var width = font.Width + PillPadding * 2;
        var height = Math.Min(rect.Height - 8, font.Height);
        var pill = new Rect(x, rect.Y + rect.Height - height - 2, width, height);
        drawingContext.DrawRoundedRectangle(_accentFill, null, pill, height / 2, height / 2);
        drawingContext.DrawRoundedRectangle(null, _accentPen, pill, height / 2, height / 2);
        drawingContext.DrawText(font, new Point(pill.X + PillPadding, pill.Y + (height - font.Height) / 2));
    }

    private string? GetExtraText(TimeLayoutItem item, ILessonControlSettings settings, IslandContext context, out bool pill, out long leftSeconds)
    {
        pill = false;
        var total = (long)item.Last.TotalSeconds;
        var elapsed = (long)(context.ExactTimeService.GetCurrentLocalDateTime().TimeOfDay - item.StartTime).TotalSeconds;
        leftSeconds = total - elapsed;

        if (!settings.ShowExtraInfoOnTimePoint)
            return null;

        if (_liveUpdating && settings.IsCountdownEnabled && leftSeconds <= settings.CountdownSeconds)
        {
            pill = true;
            return settings.IsNonExactCountdownEnabled
                ? $"< {FormatSeconds(settings.CountdownSeconds, false, true)}"
                : $"-{FormatSeconds(leftSeconds, true, false)}";
        }

        var type = _liveUpdating ? settings.ExtraInfoType : 0;
        if (settings.ExtraInfoType == 4 && leftSeconds <= settings.ExtraInfo4ShowSecondsSeconds)
            type = 5;

        return type switch
        {
            0 => $"{item.StartTime:hh\\:mm}-{item.EndTime:hh\\:mm}",
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
            var fill = new SolidColorBrush(context.AccentColor) { Opacity = 0.3 };
            fill.Freeze();
            _accentFill = fill;
            _accentPen = new Pen(_accent, 1);
            _accentPen.Freeze();
        }
    }

    private static Brush CreateChangedGlow()
    {
        var brush = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0x00)) { Opacity = 0.6 };
        brush.Freeze();
        return brush;
    }

    private void DrawChangedGlow(DrawingContext drawingContext, FormattedText text, Point origin, double radius)
    {
        var original = _foreground;
        text.SetForegroundBrush(ChangedGlow);
        for (var dx = -radius; dx <= radius; dx += radius)
        {
            for (var dy = -radius; dy <= radius; dy += radius)
            {
                if (dx == 0 && dy == 0)
                    continue;
                drawingContext.DrawText(text, new Point(origin.X + dx, origin.Y + dy));
            }
        }

        text.SetForegroundBrush(original);
    }

    private static ClassInfo? GetClassInfo(ClassPlan plan, List<TimeLayoutItem> onClassItems, TimeLayoutItem item)
    {
        var index = onClassItems.IndexOf(item);
        return index >= 0 && index < plan.Classes.Count ? plan.Classes[index] : null;
    }

    private static Subject? GetSubject(ObservableDictionary<Guid, Subject> subjects, ClassInfo? info)
    {
        if (info == null || info.SubjectId == Guid.Empty)
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
        public ILessonControlSettings Settings = null!;
    }
}
