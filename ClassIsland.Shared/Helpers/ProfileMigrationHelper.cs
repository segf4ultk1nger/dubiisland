using System;
using System.Linq;
using ClassIsland.Shared.Models.Profile;

namespace ClassIsland.Shared.Helpers;

/// <summary>
/// 档案迁移工具集
/// </summary>
public static class ProfileMigrationHelper
{
    /// <summary>
    /// 将档案中旧格式的时间点（存储在 <c>StartSecond</c>/<c>EndSecond</c> 中的日期时间）迁移到新格式的 <see cref="TimeLayoutItem.StartTime"/>/<see cref="TimeLayoutItem.EndTime"/>。
    /// </summary>
    /// <param name="profile">要迁移的档案</param>
    /// <returns>如果发生了迁移，则返回 true，否则返回 false。</returns>
    [Obsolete]
    public static bool MigrateV1TimeLayoutItems(Profile profile)
    {
        var migrated = false;
        foreach (var tl in profile.TimeLayouts.Values)
        {
            foreach (var layoutItem in tl.Layouts.Where(x =>
                         !string.IsNullOrWhiteSpace(x.StartSecond) && !string.IsNullOrWhiteSpace(x.EndSecond)))
            {
                if (DateTime.TryParse(layoutItem.StartSecond, out var s))
                {
                    layoutItem.StartTime = s.TimeOfDay;
                }
                if (DateTime.TryParse(layoutItem.EndSecond, out var e))
                {
                    layoutItem.EndTime = e.TimeOfDay;
                }
                layoutItem.StartSecond = "";
                layoutItem.EndSecond = "";
                migrated = true;
            }
        }

        return migrated;
    }
}
