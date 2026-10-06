using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Linq;
using ClassIsland.Shared.Models.Profile;

namespace ClassIsland.Controls;

public partial class TimeLineListItemExpandingAdornerControl
{
    public TimeLineListItemExpandingAdornerControl() => InitializeComponent();

    static readonly DependencyProperty TimePointProperty = DependencyProperty.Register(nameof(TimePoint),
        typeof(TimeLayoutItem), typeof(TimeLineListItemExpandingAdornerControl));

    static readonly DependencyProperty TimeLayoutProperty = DependencyProperty.Register(nameof(TimeLayout),
        typeof(ObservableCollection<TimeLayoutItem>), typeof(TimeLineListItemExpandingAdornerControl));

    static readonly DependencyProperty ScaleProperty = DependencyProperty.Register(nameof(Scale), typeof(double),
        typeof(TimeLineListItemExpandingAdornerControl));

    static readonly DependencyProperty IsReadOnlyProperty = DependencyProperty.Register(nameof(IsReadOnly),
        typeof(bool), typeof(TimeLineListItemExpandingAdornerControl));

    public TimeLayoutItem TimePoint
    {
        get => (TimeLayoutItem)GetValue(TimePointProperty);
        set => SetValue(TimePointProperty, value);
    }

    public ObservableCollection<TimeLayoutItem> TimeLayout
    {
        get => (ObservableCollection<TimeLayoutItem>)GetValue(TimeLayoutProperty);
        set => SetValue(TimeLayoutProperty, value);
    }

    public double Scale
    {
        get => (double)GetValue(ScaleProperty);
        set => SetValue(ScaleProperty, value);
    }

    public bool IsReadOnly
    {
        get => (bool)GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    public static readonly DependencyProperty IsStickyProperty = DependencyProperty.Register(
        nameof(IsSticky), typeof(bool), typeof(TimeLineListItemExpandingAdornerControl), new PropertyMetadata(default(bool)));

    public bool IsSticky
    {
        get { return (bool)GetValue(IsStickyProperty); }
        set { SetValue(IsStickyProperty, value); }
    }

    void Thumb_OnMouseEnter(object _, MouseEventArgs e) => VisualTreeUtils.FindParentVisuals<ScrollViewer>(this)
        .ForEach(static i => i.IsManipulationEnabled = false);

    void Thumb_OnMouseUp(object _, MouseEventArgs e) => VisualTreeUtils.FindParentVisuals<ScrollViewer>(this)
        .ForEach(static i => i.IsManipulationEnabled = true);

    void Thumb_OnMouseLeave(object _, MouseEventArgs e) => VisualTreeUtils.FindParentVisuals<ScrollViewer>(this)
        .ForEach(static i => i.IsManipulationEnabled = true);

    /// <summary>
    /// 获取上一个时间点。
    /// </summary>
    /// <param name="type">限定时间点类型。默认为 [0,1]，即排除分割线。</param>
    /// <returns>如果不存在，将返回 null。</returns>
    TimeLayoutItem? PrevTimePoint(int[]? type = null)
    {
        type ??= [0, 1];
        var validTimePoints = TimeLayout.Where(x => type.Contains(x.TimeType)).ToList();
        var index = validTimePoints.IndexOf(TimePoint) - 1;
        if (index >= 0 && index < validTimePoints.Count)
        {
            return validTimePoints[index];
        }

        return null;
    }

    TimeLayoutItem? PrevTimePoint(int type) => PrevTimePoint([type]);

    /// <summary>
    /// 获取下一个时间点。
    /// </summary>
    /// <param name="type">限定时间点类型。默认为 [0,1]，即排除分割线。</param>
    /// <returns>如果不存在，将返回 null。</returns>
    TimeLayoutItem? NextTimePoint(int[]? type = null)
    {
        type ??= [0, 1];
        var validTimePoints = TimeLayout.Where(x => type.Contains(x.TimeType)).ToList();
        var index = validTimePoints.IndexOf(TimePoint) + 1;
        if (index >= 0 && index < validTimePoints.Count)
        {
            return validTimePoints[index];
        }

        return null;
    }

    TimeLayoutItem? NextTimePoint(int type) => NextTimePoint([type]);

    /// <summary>
    /// 调整周边的分割线。<br/>
    /// 此方法的存在是因为分割线在时间表中的位置不确定。
    /// </summary>
    /// <param name="oldTime">调整前的时间</param>
    /// <param name="newTime">调整后的时间</param>
    void DragAdjoiningSeparator(TimeSpan oldTime, TimeSpan newTime)
    {
        foreach (var s in (from s in TimeLayout where s.TimeType == 2 select s).Where(s =>
                     s.StartTime == oldTime))
            s.StartTime = s.EndTime = newTime;
    }

    static TimeSpan RoundTime(TimeSpan time) => TimeSpan.FromMinutes(time.TotalMinutes - (time.TotalMinutes % 5));
    TimeSpan GetDelta(TimeSpan raw, double v) => RoundTime(TimeSpan.FromSeconds(v * 100 / Scale) + raw) - raw;

    void ThumbTop_OnDragDelta(object _, DragDeltaEventArgs e)
    {
        var a = TimePoint.StartTime;
        var b = PrevTimePoint();
        var d = GetDelta(a, e.VerticalChange);
        if (TimePoint.EndTime > a + d && (!(a + d < PrevTimePoint()?.EndTime) || IsSticky))
        {
            var newStart = TimePoint.StartTime + d;
            if (!IsSticky)
            {
                TimePoint.StartTime = newStart;
                return;
            }
            if (b != null && b.StartTime >= newStart)
            {
                return;
            }
            if (b != null && b.EndTime == a)
                b.EndTime = newStart;
            TimePoint.StartTime = newStart;

            // else if (b?.TimeType == 0)
            //     App.GetService<ProfileSettingsWindow>().AddTimeLayoutItem(1, TimePoint.StartSecond, b.EndSecond);
            DragAdjoiningSeparator(a, TimePoint.StartTime);
        }
    }

    void ThumbBottom_OnDragDelta(object sender, DragDeltaEventArgs e)
    {
        var a = TimePoint.EndTime;
        var b = NextTimePoint();
        var d = GetDelta(a, e.VerticalChange);
        if (a + d > TimePoint.StartTime && (!(NextTimePoint()?.StartTime < a + d) || IsSticky))
        {
            var newEnd = TimePoint.EndTime + d;
            if (!IsSticky)
            {
                TimePoint.EndTime = newEnd;
                return;
            }
            if (b != null && b.EndTime <= newEnd)
            {
                return;
            }
            if (b != null && b.StartTime == a)
                b.StartTime = newEnd;
            TimePoint.EndTime = newEnd;
            // else if (b?.TimeType == 0)
            //     App.GetService<ProfileSettingsWindow>().AddTimeLayoutItem(1, b.StartSecond, TimePoint.EndSecond);
            DragAdjoiningSeparator(a, TimePoint.EndTime);
        }
    }

    void Thumb_OnDragDelta(object sender, DragDeltaEventArgs e)
    {
        var a1 = TimePoint.StartTime;
        var a2 = TimePoint.EndTime;
        var d = GetDelta(a1, e.VerticalChange);

        if (!IsSticky && (a1 + d < PrevTimePoint()?.EndTime || NextTimePoint()?.StartTime < a2 + d))
            return;

        var newStart = TimePoint.StartTime + d;
        var newEnd = TimePoint.EndTime + d;
        if (!IsSticky)
        {
            TimePoint.StartTime = newStart;
            TimePoint.EndTime = newEnd;
            return;
        }
        var b1 = PrevTimePoint();
        var b2 = NextTimePoint();
        //Console.WriteLine($"{a1} {b1?.EndTime} {newStart}");
        if (b1 != null && b1.StartTime >= newStart)
        {
            return;
        }
        if (b2 != null && b2.EndTime <= newEnd)
        {
            return;
        }
        if (b1 != null && b1.EndTime == a1)
        {
            b1.EndTime = newStart;
        }

        // else if (b1?.TimeType == 0)
        //     App.GetService<ProfileSettingsWindow>().AddTimeLayoutItem(1, b1.EndSecond, TimePoint.StartSecond);
        if (b2 != null && b2.StartTime == a2)
        {
            b2.StartTime = newEnd;
        }

        // else if (b2?.TimeType == 0)
        //     App.GetService<ProfileSettingsWindow>().AddTimeLayoutItem(1, TimePoint.EndSecond, b2.StartSecond);
        TimePoint.StartTime = newStart;
        TimePoint.EndTime = newEnd;
        DragAdjoiningSeparator(a1, TimePoint.StartTime);
        DragAdjoiningSeparator(a2, TimePoint.EndTime);
        // App.GetService<ProfileSettingsWindow>().UpdateTimeLayout();
    }
}