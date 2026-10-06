using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Abstractions.Services.Management;
using ClassIsland.Shared.Models.Profile;

using Microsoft.Extensions.Logging;

using static ClassIsland.Shared.Helpers.ConfigureFileHelper;

using Path = System.IO.Path;
using ClassIsland.Shared;

namespace ClassIsland.Services;

public class ProfileService : IProfileService, INotifyPropertyChanged
{
    public string CurrentProfilePath { 
        get; 
        set;
    } = Path.Combine(App.AppRootFolderPath, @"Default.json");

    public static readonly string ManagementClassPlanPath =
        Path.Combine(Management.ManagementService.ManagementConfigureFolderPath, "ClassPlans.json");

    public static readonly string ManagementTimeLayoutPath =
        Path.Combine(Management.ManagementService.ManagementConfigureFolderPath, "TimeLayouts.json");

    public static readonly string ManagementSubjectsPath =
        Path.Combine(Management.ManagementService.ManagementConfigureFolderPath, "Subjects.json");

    public static readonly string ProfilePath = Path.Combine(App.AppRootFolderPath, "Profiles");

    public Profile Profile {
        get;
        set;
    } = new Profile();

    private SettingsService SettingsService { get; }

    private ILogger<ProfileService> Logger { get; }

    private IManagementService ManagementService { get; }

    private bool _isProfileLoaded = false;
    private bool _isCurrentProfileTrusted = false;

    public ProfileService(SettingsService settingsService, ILogger<ProfileService> logger, IManagementService managementService)
    {
        Logger = logger;
        ManagementService = managementService;
        SettingsService = settingsService;
        if (!Directory.Exists(ProfilePath))
        {
            Directory.CreateDirectory(ProfilePath);
        }
    }


    private async Task MergeManagementProfileAsync()
    {
        Logger.LogInformation("正在拉取集控档案");
        if (ManagementService.Connection == null)
            return;
        try
        {
            Profile? classPlan = null;
            Profile? timeLayouts = null;
            Profile? subjects = null;
            if (ManagementService.Manifest.ClassPlanSource.IsNewerAndNotNull(ManagementService.Versions.ClassPlanVersion))
            {
                var cpOld = LoadConfig<Profile>(ManagementClassPlanPath);
                var cpNew = classPlan = await ManagementService.Connection.GetJsonAsync<Profile>(ManagementService.Manifest.ClassPlanSource.Value!);
                MergeDictionary(Profile.ClassPlans, cpOld.ClassPlans, cpNew.ClassPlans);
                MergeDictionary(Profile.ClassPlanGroups, cpOld.ClassPlanGroups, cpNew.ClassPlanGroups);
            }
            if (ManagementService.Manifest.TimeLayoutSource.IsNewerAndNotNull(ManagementService.Versions.TimeLayoutVersion))
            {
                var tlOld = LoadConfig<Profile>(ManagementTimeLayoutPath);
                var tlNew = timeLayouts = await ManagementService.Connection.GetJsonAsync<Profile>(ManagementService.Manifest.TimeLayoutSource.Value!);
                MergeDictionary(Profile.TimeLayouts, tlOld.TimeLayouts, tlNew.TimeLayouts);
            }
            if (ManagementService.Manifest.SubjectsSource.IsNewerAndNotNull(ManagementService.Versions.SubjectsVersion))
            {
                var subjectOld = LoadConfig<Profile>(ManagementSubjectsPath);
                var subjectNew = subjects = await ManagementService.Connection.GetJsonAsync<Profile>(ManagementService.Manifest.SubjectsSource.Value!);
                MergeDictionary(Profile.Subjects, subjectOld.Subjects, subjectNew.Subjects);
            }

            SaveProfile("_management-profile.json");
            ManagementService.Versions.ClassPlanVersion = ManagementService.Manifest.ClassPlanSource.Version;
            ManagementService.Versions.TimeLayoutVersion = ManagementService.Manifest.TimeLayoutSource.Version;
            ManagementService.Versions.SubjectsVersion = ManagementService.Manifest.SubjectsSource.Version;
            ManagementService.SaveSettings();
        }
        catch (Exception exp)
        {
            Logger.LogError(exp, "拉取档案失败。");
        }

        
        //Profile = ConfigureFileHelper.CopyObject(Profile);
        Profile.Subjects = CopyObject(Profile.Subjects);
        Profile.TimeLayouts = CopyObject(Profile.TimeLayouts);
        Profile.ClassPlans = CopyObject(Profile.ClassPlans);
        Profile.RefreshTimeLayouts();
        Logger.LogTrace("成功拉取集控档案！");
    }

    public async Task LoadProfileAsync()
    {
        var filename = ManagementService.IsManagementEnabled ? "_management-profile.json" : SettingsService.Settings.SelectedProfile;
        var path = Path.Combine(ProfilePath, filename);
        Logger.LogInformation("加载档案中：{}", path);
        if (!File.Exists(path))
        {
            Logger.LogInformation("档案不存在：{}", path);
            if (!ManagementService.IsManagementEnabled)  // 在集控模式下不需要默认科目
            {
                var subject = new StreamReader(Application.GetResourceStream(new Uri("/Assets/default-subjects.json", UriKind.Relative))!.Stream).ReadToEnd();
                Profile.Subjects = JsonSerializer.Deserialize<Profile>(subject)!.Subjects;
            }
            SaveProfile(filename);
        }

        var r = LoadConfig<Profile>(path);

        Profile = r;
        if (ManagementService.IsManagementEnabled)
        {
            await MergeManagementProfileAsync();
        }
        Profile.PropertyChanged += (sender, args) => SaveProfile(filename);

        if (SettingsService.Settings.TrustedProfileIds.Contains(Profile.Id))
        {
            IsCurrentProfileTrusted = true;
        }

        if (SettingsService.WillMigrateProfileTrustedState)
        {
            TrustCurrentProfile();
            SettingsService.WillMigrateProfileTrustedState = false;
            Logger.LogInformation("自动信任来自 1.5.4.0 以前的当前档案。");
        }
        CurrentProfilePath = filename;
        Logger.LogTrace("成功加载档案！信任：{}", IsCurrentProfileTrusted);
        CleanExpiredTempClassPlan();
        _isProfileLoaded = true;
    }

    public void SaveProfile()
    {
        if (!_isProfileLoaded)
        {
            return;
        }
        if (CurrentProfilePath.Contains(".\\Profiles\\"))
        {
            var splittedFileName = CurrentProfilePath.Split("\\");
            var fileName = splittedFileName[splittedFileName.Length - 1];
            SaveProfile(fileName);
            return;
        }
        SaveProfile(CurrentProfilePath);
    }

    public void SaveProfile(string filename)
    {
        Logger.LogInformation("写入档案文件：{}", Path.Combine(ProfilePath, filename));
        SaveConfig(Path.Combine(ProfilePath, filename), Profile);
    }

    private static T DuplicateJson<T>(T o)
    {
        var json = JsonSerializer.Serialize(o);
        return JsonSerializer.Deserialize<T>(json)!;
    }

    public Guid? CreateTempClassPlan(Guid id, Guid? timeLayoutId=null, DateTime? enableDateTime = null)
    {
        Logger.LogInformation("创建临时层：{}", id);
        var date = enableDateTime ?? IAppHost.GetService<IExactTimeService>().GetCurrentLocalDateTime().Date;
        if (Profile.OrderedSchedules.TryGetValue(date, out var orderedSchedule)
            && Guid.TryParse(orderedSchedule.ClassPlanId, out var orderedClassPlanId)
            && Profile.ClassPlans.TryGetValue(orderedClassPlanId, out var cp1)
            && cp1.IsOverlay)
        {
            return null;
        }
        var cp = Profile.ClassPlans[id];
        timeLayoutId ??= cp.TimeLayoutId;
        var newCp = DuplicateJson(cp);

        newCp.IsOverlay = true;
        newCp.TimeLayoutId = timeLayoutId.Value;
        newCp.OverlaySourceId = id;
        newCp.Name += "（临时层）";
        newCp.OverlaySetupTime = date;
        Profile.IsOverlayClassPlanEnabled = true;
        var newId = Guid.NewGuid();
        Profile.OverlayClassPlanId = newId;
        Profile.ClassPlans.Add(newId, newCp);
        Profile.OrderedSchedules[date] = new OrderedSchedule()
        {
            ClassPlanId = newId.ToString()
        };
        return newId;
    }

    public void ClearTempClassPlan()
    {
        if (Profile.OverlayClassPlanId == null || !Profile.ClassPlans.ContainsKey(Profile.OverlayClassPlanId ?? Guid.Empty))
        {
            return;
        }

        Logger.LogInformation("清空今天的临时层：{}", Profile.OverlayClassPlanId);
        Profile.OrderedSchedules.Remove(IAppHost.GetService<IExactTimeService>().GetCurrentLocalDateTime().Date);
        Profile.OverlayClassPlanId = null;
        CleanExpiredTempClassPlan();
    }

    public void CleanExpiredTempClassPlan()
    {
        var today = IAppHost.GetService<IExactTimeService>().GetCurrentLocalDateTime().Date;
        foreach (var schedule in Profile.OrderedSchedules
                     .Where(x => x.Key < today)
                     .ToList())
        {
            Profile.OrderedSchedules.Remove(schedule.Key);
            Logger.LogInformation("清理过期的课表预定：{}", schedule.Key);
        }

        var orderedSchedules = Profile.OrderedSchedules
            .Select(x => Guid.TryParse(x.Value.ClassPlanId, out var id) ? id : Guid.Empty)
            .ToList();

        foreach (var classPlan in Profile.ClassPlans.Where(x => x.Value.IsOverlay).ToList())
        {
            if (orderedSchedules.Contains(classPlan.Key))
                continue;
            Profile.ClassPlans.Remove(classPlan.Key);
            Logger.LogInformation("清理没有被引用的过期临时层课表：{}", classPlan.Key);
        }
    }

    //[Obsolete]
    //public bool CheckClassPlan(ClassPlan plan)
    //{
    //}

    public void ConvertToStdClassPlan()
    {
        Logger.LogInformation("将当前临时层课表转换为普通课表：{}", Profile.OverlayClassPlanId);
        if (Profile.OverlayClassPlanId != null)
        {
            ConvertToStdClassPlan(Profile.OverlayClassPlanId.Value);
        }
    }

    public void ConvertToStdClassPlan(Guid id)
    {
        Logger.LogInformation("将临时层课表转换为普通课表：{}", id);
        var today = IAppHost.GetService<IExactTimeService>().GetCurrentLocalDateTime().Date;
        if (!Profile.ClassPlans.TryGetValue(id, out var classPlan))
        {
            return;
        }
        classPlan.IsOverlay = false;
    }

    public void SetupTempClassPlanGroup(Guid key, DateTime? expireTime = null)
    {
        var classPlans = Profile.ClassPlans
            .Where(x => x.Value.AssociatedGroup == key)
            .Select(x => x.Value);
        var today = App.GetService<IExactTimeService>().GetCurrentLocalDateTime();
        var dow = today.DayOfWeek;
        var dayOffset = 0;
        var dd = today.Date - SettingsService.Settings.SingleWeekStartTime.Date;
        var dw = Math.Floor(dd.TotalDays / 7) + 1;
        foreach (var classPlan in classPlans)
        {
            var w = (int)dw % classPlan.TimeRule.WeekCountDivTotal;
            var baseOffset = (int)(classPlan.TimeRule.WeekDay - dow);
            var divOffset = (classPlan.TimeRule.WeekCountDiv + classPlan.TimeRule.WeekCountDivTotal - w) % classPlan.TimeRule.WeekCountDivTotal;
            var finalOffset = baseOffset + (divOffset * 7);
            if (finalOffset < 0)
            {
                finalOffset += 7;
            }

            dayOffset = Math.Max(finalOffset, dayOffset);
        }
        expireTime ??= App.GetService<IExactTimeService>().GetCurrentLocalDateTime().Date + TimeSpan.FromDays(dayOffset);

        Profile.TempClassPlanGroupExpireTime = expireTime.Value;
        Profile.TempClassPlanGroupId = key;
        Profile.IsTempClassPlanGroupEnabled = true;
    }

    public void ClearTempClassPlanGroup()
    {
        Profile.TempClassPlanGroupId = null;
        Profile.IsTempClassPlanGroupEnabled = false;
    }

    public bool IsCurrentProfileTrusted
    {
        get => _isCurrentProfileTrusted;
        private set
        {
            if (value == _isCurrentProfileTrusted) return;
            _isCurrentProfileTrusted = value;
            OnPropertyChanged();
        }
    }

    public void ClearExpiredTempClassPlanGroup()
    {
        if (Profile.TempClassPlanGroupExpireTime.Date < App.GetService<IExactTimeService>().GetCurrentLocalDateTime().Date)
        {
            ClearTempClassPlanGroup();
        }
    }

    public void TrustCurrentProfile()
    {
        SettingsService.Settings.TrustedProfileIds.Add(Profile.Id);
        IsCurrentProfileTrusted = true;
        Logger.LogInformation("已信任当前档案 {}", Profile.Id);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}