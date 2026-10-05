using System;
using System.Text.Json;
using ClassIsland.Controls.Island.Components;
using ClassIsland.Core.Models.Components;

namespace ClassIsland.Controls.Island;

/// <summary>
/// 把 <see cref="ComponentSettings"/>（组件配置）映射到具体的自绘组件实现。
/// </summary>
public static class IslandComponentFactory
{
    public const string DateId = "DF3F8295-21F6-482E-BADA-FA0E5F14BB66";
    public const string ClockId = "9E1AF71D-8F77-4B21-A342-448787104DD9";
    public const string TextId = "EE8F66BD-C423-4E7C-AB46-AA9976B00E08";
    public const string CountDownId = "7C645D35-8151-48BA-B4AC-15017460D994";
    public const string SeparatorId = "AB0F26D5-9DF6-4575-B844-73B04D0907C1";
    public const string WeatherId = "CA495086-E297-4BEB-9603-C5C1C1A8551E";
    public const string ScheduleId = "1DB2017D-E374-4BC6-9D57-0B4ADF03A6B8";
    public const string GroupId = "C911D762-107F-40C6-84CC-0146AB3C86B1";
    public const string RollingId = "70FCD5EA-3FAE-4E06-ACA2-4F4DF47F9ACD";
    public const string SlideId = "7E19A113-D281-4F33-970A-834A0B78B5AD";

    public static IIslandComponent? Create(ComponentSettings component, IslandContext context)
    {
        return component.Id.ToUpperInvariant() switch
        {
            DateId => new DateIslandComponent(component),
            ClockId => new ClockIslandComponent(component),
            TextId => new TextIslandComponent(component),
            CountDownId => new CountDownIslandComponent(component),
            SeparatorId => new SeparatorIslandComponent(component),
            WeatherId => new WeatherIslandComponent(component),
            ScheduleId => new ScheduleIslandComponent(component, GetSettings<Models.ComponentSettings.LessonControlSettings>(component)),
            GroupId => new GroupIslandComponent(component, context),
            RollingId => new RollingIslandComponent(component, context),
            SlideId => new SlideIslandComponent(component, context),
            _ => null
        };
    }

    /// <summary>取得（必要时反序列化）组件自己的设置对象。</summary>
    public static T GetSettings<T>(ComponentSettings component) where T : class, new()
    {
        switch (component.Settings)
        {
            case T typed:
                return typed;
            case JsonElement json:
                try
                {
                    var value = json.Deserialize<T>();
                    if (value != null)
                    {
                        component.Settings = value;
                        return value;
                    }
                }
                catch
                {
                    // 反序列化失败时回退到默认值
                }
                break;
        }

        var fallback = new T();
        component.Settings = fallback;
        return fallback;
    }
}
