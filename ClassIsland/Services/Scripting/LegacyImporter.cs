using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Shared.Helpers;
using ClassIsland.Shared.Models.Profile;
using Microsoft.Extensions.Logging;

namespace ClassIsland.Services.Scripting;

/// <summary>
/// 一次性旧配置导入结果报告。
/// </summary>
public sealed class LegacyImportReport
{
    /// <summary>成功转换的条目数。</summary>
    public int Converted { get; set; }

    /// <summary>跳过的条目数。</summary>
    public int Skipped { get; set; }

    /// <summary>未能映射的规则/触发器/行动 ID 列表。</summary>
    public List<string> Unmapped { get; } = new();

    /// <summary>导入过程中的警告。</summary>
    public List<string> Warnings { get; } = new();

    /// <summary>是否存在未映射项，需要人工检查。</summary>
    public bool NeedsReview => Unmapped.Count > 0;

    /// <summary>
    /// 生成用于对话框显示的文本。
    /// </summary>
    public string ToDisplayString()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"转换完成：{Converted} 项，跳过 {Skipped} 项。");
        sb.AppendLine(NeedsReview ? "存在未映射项，请人工检查。" : "未发现未映射项。");
        AppendList(sb, "未映射", Unmapped);
        AppendList(sb, "警告", Warnings);
        return sb.ToString();

        static void AppendList(StringBuilder sb, string title, List<string> items)
        {
            if (items.Count == 0)
            {
                return;
            }

            sb.AppendLine();
            sb.AppendLine($"{title} ({items.Count})：");
            foreach (var item in items.Take(50))
            {
                sb.AppendLine("  - " + item);
            }

            if (items.Count > 50)
            {
                sb.AppendLine($"  … 其余 {items.Count - 50} 项已省略。");
            }
        }
    }
}

/// <summary>
/// 将旧版 Ruleset/自动化/行动配置转换为脚本与条件表达式的一次性导入器。
/// 全部使用私有 DTO，不引用将被移除的旧模型类型。
/// </summary>
public sealed class LegacyImporter
{
    private const int ModeAnd = 1;
    private const int TimeTypeAction = 3;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonSerializerOptions JsStringOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly string[] TimeStateNames =
        { "None", "OnClass", "PrepareOnClass", "Breaking", "AfterSchool" };

    private readonly ILogger<LegacyImporter> _logger;
    private readonly SettingsService _settingsService;
    private readonly IComponentsService _componentsService;
    private readonly IProfileService _profileService;
    private readonly ScriptRuntimeService _runtime;

    /// <summary>
    /// 初始化一个 <see cref="LegacyImporter"/> 实例。
    /// </summary>
    public LegacyImporter(ILogger<LegacyImporter> logger, SettingsService settingsService,
        IComponentsService componentsService, IProfileService profileService, ScriptRuntimeService runtime)
    {
        _logger = logger;
        _settingsService = settingsService;
        _componentsService = componentsService;
        _profileService = profileService;
        _runtime = runtime;
    }

    /// <summary>
    /// 执行导入。可重复运行：已转换的内容不会再次处理。
    /// </summary>
    public LegacyImportReport Import()
    {
        var report = new LegacyImportReport();
        try
        {
#if DEBUG
            MappingSelfTest();
#endif
            var scriptsDir = ScriptRuntimeService.ScriptDirectory;
            Directory.CreateDirectory(scriptsDir);

            var manifest = ConfigureFileHelperBridge.Load(scriptsDir);

            var resolver = SubjectResolver(_profileService.Profile);
            var workflowIndex = MaxExistingIndex(scriptsDir, "legacy-workflow-", ".js");

            ImportAutomations(report, scriptsDir, manifest, resolver, ref workflowIndex);
            ImportSettings(report);
            ImportComponentLayouts(report, resolver);
            ImportProfiles(report, scriptsDir, manifest, resolver);

            ConfigureFileHelperBridge.Save(scriptsDir, manifest);
            _runtime.ReloadAll();
            _logger.LogInformation("旧配置导入完成：转换 {Converted}，跳过 {Skipped}，未映射 {Unmapped}。",
                report.Converted, report.Skipped, report.Unmapped.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "旧配置导入过程中发生异常。");
            report.Warnings.Add("导入异常：" + ex.Message);
        }

        return report;
    }

    // ============================ 自动化 ============================

    private void ImportAutomations(LegacyImportReport report, string scriptsDir, ScriptManifest manifest,
        Func<Guid, string?> resolver, ref int workflowIndex)
    {
        var dir = Path.Combine(App.AppConfigPath, "Automations");
        if (!Directory.Exists(dir))
        {
            return;
        }

        foreach (var path in Directory.GetFiles(dir, "*.json"))
        {
            List<DtoWorkflow> workflows;
            try
            {
                workflows = JsonSerializer.Deserialize<List<DtoWorkflow>>(File.ReadAllText(path), JsonOptions)
                            ?? new List<DtoWorkflow>();
            }
            catch (Exception ex)
            {
                report.Warnings.Add($"无法解析自动化配置 {Path.GetFileName(path)}：{ex.Message}");
                report.Skipped++;
                continue;
            }

            var configName = Path.GetFileNameWithoutExtension(path);
            foreach (var workflow in workflows)
            {
                var localUnmapped = new List<string>();
                var index = ++workflowIndex;
                var script = BuildWorkflowScript(workflow, resolver, index, localUnmapped, report.Warnings);
                if (script == null)
                {
                    report.Skipped++;
                    MergeUnmapped(report, localUnmapped, configName);
                    continue;
                }

                var fileName = $"legacy-workflow-{index}.js";
                File.WriteAllText(Path.Combine(scriptsDir, fileName), script);
                EnsureManifestEntry(manifest, fileName, workflow.ActionSet?.Name ?? configName,
                    workflow.ActionSet?.IsEnabled ?? true);
                report.Converted++;
                MergeUnmapped(report, localUnmapped, configName);
            }

            try
            {
                var bak = path + ".bak";
                File.Copy(path, bak, true);
                File.Delete(path);
            }
            catch (Exception ex)
            {
                report.Warnings.Add($"无法将自动化配置 {Path.GetFileName(path)} 重命名为 .bak：{ex.Message}");
            }
        }
    }

    /// <summary>
    /// 将一个旧工作流转换为脚本文本；无可用触发器或行动组时返回 null。
    /// </summary>
    private static string? BuildWorkflowScript(DtoWorkflow workflow, Func<Guid, string?> subjectName, int index,
        List<string> unmapped, List<string> warnings)
    {
        if (workflow.ActionSet == null)
        {
            warnings.Add("工作流没有行动组，已跳过。");
            return null;
        }

        var handlerVar = $"__workflow_{index}";
        var registrations = new List<string>();
        foreach (var trigger in workflow.Triggers ?? new List<DtoTrigger>())
        {
            var line = MapTrigger(trigger, handlerVar, warnings);
            if (line != null)
            {
                registrations.Add(line);
            }
        }

        if (registrations.Count == 0)
        {
            warnings.Add($"工作流“{workflow.ActionSet.Name}”没有可用的触发器，已跳过。");
            return null;
        }

        var condition = workflow.IsConditionEnabled ? MapRuleset(workflow.Ruleset, subjectName, unmapped) : null;
        var doLines = new List<string>();
        var undoLines = new List<string>();
        var needAwait = false;
        CollectActions(workflow.ActionSet, doLines, undoLines, ref needAwait, unmapped, warnings);

        var properties = new List<string>();
        if (condition != null)
        {
            properties.Add($"    when: () => ({condition})");
        }

        properties.Add(BuildFunction("do", needAwait, doLines));
        if (undoLines.Count > 0)
        {
            properties.Add(BuildFunction("undo", false, undoLines));
        }

        var sb = new StringBuilder();
        sb.AppendLine($"// 由 LegacyIsland 旧自动化导入：{workflow.ActionSet.Name}");
        sb.AppendLine($"var {handlerVar} = {{");
        sb.AppendLine(string.Join(",\n", properties));
        sb.AppendLine("};");
        foreach (var registration in registrations)
        {
            sb.AppendLine(registration);
        }

        AppendUnmappedComments(sb, unmapped);
        return sb.ToString();
    }

    private static void CollectActions(DtoActionSet actionSet, List<string> doLines, List<string> undoLines,
        ref bool needAwait, List<string> unmapped, List<string> warnings)
    {
        foreach (var action in actionSet.Actions ?? new List<DtoAction>())
        {
            if (string.IsNullOrEmpty(action.Id))
            {
                continue;
            }

            if (!action.IsEnabled)
            {
                doLines.Add($"// [已禁用] {action.Id}");
                continue;
            }

            var mapping = MapAction(action, warnings);
            if (mapping == null)
            {
                if (!unmapped.Contains(action.Id))
                {
                    unmapped.Add(action.Id);
                }

                doLines.Add($"// [未映射] {action.Id}");
                continue;
            }

            doLines.Add(mapping.Value.Do);
            if (mapping.Value.NeedAwait)
            {
                needAwait = true;
            }

            if (actionSet.IsRevertEnabled && mapping.Value.Undo != null)
            {
                undoLines.Add(mapping.Value.Undo);
            }
        }
    }

    private static string? MapTrigger(DtoTrigger trigger, string handlerVar, List<string> warnings)
    {
        switch (trigger.Id)
        {
            case "classisland.lifetime.startup":
                return $"on.startup({handlerVar});";
            case "classisland.lifetime.stopping":
                return $"on.stopping({handlerVar});";
            case "classisland.lessons.onClass":
                return $"on.classStart({handlerVar});";
            case "classisland.lessons.onBreakingTime":
                return $"on.breakingTime({handlerVar});";
            case "classisland.lessons.onAfterSchool":
                return $"on.afterSchool({handlerVar});";
            case "classisland.lessons.currentTimeStateChanged":
                return $"on.timeStateChanged({handlerVar});";
            case "classisland.ruleSet.rulesetChanged":
                return $"on.conditionChanged({handlerVar});";
            case "classisland.cron":
            {
                var s = Get<DtoCron>(trigger.Settings) ?? new DtoCron();
                return $"on.cron({Js(s.CronExpression)}, {handlerVar});";
            }
            case "classisland.signal":
            {
                var s = Get<DtoSignal>(trigger.Settings) ?? new DtoSignal();
                return $"on.signal({Js(s.SignalName)}, {handlerVar});";
            }
            case "classisland.uri":
            {
                var s = Get<DtoUri>(trigger.Settings) ?? new DtoUri();
                return $"on.uri({Js(s.UriSuffix)}, {handlerVar});";
            }
            case "classisland.lessons.preTimePoint":
            {
                var s = Get<DtoPreTimePoint>(trigger.Settings) ?? new DtoPreTimePoint();
                return $"on.preTimePoint({{ state: '{PreStateName(s.TargetState)}', seconds: {Num(s.TimeSeconds)} }}, {handlerVar});";
            }
            default:
                warnings.Add($"未映射的触发器：{trigger.Id}");
                return null;
        }
    }

    private static (string Do, string? Undo, bool NeedAwait)? MapAction(DtoAction action, List<string> warnings)
    {
        switch (action.Id)
        {
            case "classisland.os.run":
            {
                var s = Get<DtoRun>(action.Settings) ?? new DtoRun();
                return ($"run({Js(s.Value)}, {Js(s.Args)});", null, false);
            }
            case "classisland.action.sleep":
            {
                var s = Get<DtoSleep>(action.Settings) ?? new DtoSleep();
                return ($"await sleep({Num(s.Value)});", null, true);
            }
            case "classisland.settings.theme":
            {
                var s = Get<DtoIntValue>(action.Settings) ?? new DtoIntValue();
                return ($"setTheme({s.Value});", "clearTheme();", false);
            }
            case "classisland.settings.windowDockingLocation":
            {
                var s = Get<DtoIntValue>(action.Settings) ?? new DtoIntValue();
                return ($"setWindowDockingLocation({s.Value});", "clearWindowDockingLocation();", false);
            }
            case "classisland.settings.windowLayer":
            {
                var s = Get<DtoIntValue>(action.Settings) ?? new DtoIntValue();
                return ($"setWindowLayer({s.Value});", "clearWindowLayer();", false);
            }
            case "classisland.settings.windowDockingOffsetX":
            {
                var s = Get<DtoIntValue>(action.Settings) ?? new DtoIntValue();
                return ($"setWindowDockingOffsetX({s.Value});", "clearWindowDockingOffsetX();", false);
            }
            case "classisland.settings.windowDockingOffsetY":
            {
                var s = Get<DtoIntValue>(action.Settings) ?? new DtoIntValue();
                return ($"setWindowDockingOffsetY({s.Value});", "clearWindowDockingOffsetY();", false);
            }
            case "classisland.settings.currentComponentConfig":
            {
                var s = Get<DtoStringValue>(action.Settings) ?? new DtoStringValue();
                return ($"switchComponentConfig({Js(s.Value)});", "clearComponentConfig();", false);
            }
            case "classisland.showNotification":
            {
                var s = Get<DtoNotification>(action.Settings) ?? new DtoNotification();
                if (!string.IsNullOrEmpty(s.CustomSoundEffectPath))
                {
                    warnings.Add($"提醒行动使用了自定义音效“{s.CustomSoundEffectPath}”，脚本运行时暂不支持自定义音效路径。");
                }

                var speech = s.IsContentSpeechEnabled || s.IsMaskSpeechEnabled;
                var soundPath = string.IsNullOrEmpty(s.CustomSoundEffectPath)
                    ? ""
                    : $", soundPath: {Js(s.CustomSoundEffectPath)}";
                var expr =
                    $"notify({{ content: {Js(s.Content)}, mask: {Js(s.Mask)}, " +
                    $"duration: {Num(s.ContentDurationSeconds)}, contentDuration: {Num(s.ContentDurationSeconds)}, " +
                    $"maskDuration: {Num(s.MaskDurationSeconds)}, speech: {Bool(speech)}, sound: {Bool(s.IsSoundEffectEnabled)}, " +
                    $"effect: {Bool(s.IsEffectEnabled)}, topmost: {Bool(s.IsTopmostEnabled)}{soundPath} }});";
                return (expr, null, false);
            }
            case "classisland.notification.weather":
            {
                var s = Get<DtoWeatherNotify>(action.Settings) ?? new DtoWeatherNotify();
                return ($"weatherNotify({s.NotificationKind});", null, false);
            }
            case "classisland.broadcastSignal":
            {
                var s = Get<DtoSignal>(action.Settings) ?? new DtoSignal();
                return ($"broadcast({Js(s.SignalName)}, {Bool(s.IsRevert)});",
                    $"broadcast({Js(s.SignalName)}, {Bool(!s.IsRevert)});", false);
            }
            case "classisland.app.quit":
                return ("quit();", null, false);
            case "classisland.app.restart":
            {
                var s = Get<DtoBoolValue>(action.Settings) ?? new DtoBoolValue();
                return ($"restart({Bool(s.Value)});", null, false);
            }
            default:
                return null;
        }
    }

    // ============================ 时间点 ============================

    private void ImportProfiles(LegacyImportReport report, string scriptsDir, ScriptManifest manifest,
        Func<Guid, string?> fallbackResolver)
    {
        var dir = Path.Combine(App.AppRootFolderPath, "Profiles");
        if (!Directory.Exists(dir))
        {
            return;
        }

        var currentName = Path.GetFileName(_profileService.CurrentProfilePath);
        var currentPath = Path.Combine(dir, currentName);
        if (!string.IsNullOrEmpty(currentName) && File.Exists(currentPath))
        {
            ImportCurrentProfile(currentPath, report, scriptsDir, manifest);
        }

        foreach (var path in Directory.GetFiles(dir, "*.json"))
        {
            if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(currentPath), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            TransformProfileFile(path, report, scriptsDir, manifest, fallbackResolver);
        }
    }

    private void ImportCurrentProfile(string path, LegacyImportReport report, string scriptsDir, ScriptManifest manifest)
    {
        JsonObject? root;
        try
        {
            root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
        }
        catch (Exception ex)
        {
            report.Warnings.Add($"无法解析档案 {Path.GetFileName(path)}：{ex.Message}");
            return;
        }

        if (root == null)
        {
            return;
        }

        var resolver = SubjectResolver(_profileService.Profile);
        var modifications = new List<TimePointModification>();
        var converted = ConvertProfileTimePoints(root, Path.GetFileNameWithoutExtension(path), resolver, scriptsDir,
            manifest, modifications, report, out var unmapped);
        MergeUnmapped(report, unmapped, Path.GetFileName(path));

        if (converted == 0)
        {
            return;
        }

        // 反射访问待删除的 ActionSet 属性，避免编译期引用旧类型。
        var actionSetProperty = typeof(TimeLayoutItem).GetProperty("ActionSet");
        foreach (var mod in modifications)
        {
            if (!_profileService.Profile.TimeLayouts.TryGetValue(mod.TimeLayoutId, out var layout)
                || mod.ItemIndex < 0 || mod.ItemIndex >= layout.Layouts.Count)
            {
                continue;
            }

            var item = layout.Layouts[mod.ItemIndex];
            item.Script = mod.FileName;
            actionSetProperty?.SetValue(item, null);
        }

        Backup(path);
        _profileService.SaveProfile();
        report.Converted += converted;
    }

    private void TransformProfileFile(string path, LegacyImportReport report, string scriptsDir,
        ScriptManifest manifest, Func<Guid, string?> fallbackResolver)
    {
        JsonObject? root;
        string text;
        try
        {
            text = File.ReadAllText(path);
            root = JsonNode.Parse(text) as JsonObject;
        }
        catch (Exception ex)
        {
            report.Warnings.Add($"无法解析档案 {Path.GetFileName(path)}：{ex.Message}");
            return;
        }

        if (root == null)
        {
            return;
        }

        var resolver = SubjectResolverFromJson(root) ?? fallbackResolver;
        var modifications = new List<TimePointModification>();
        var converted = ConvertProfileTimePoints(root, Path.GetFileNameWithoutExtension(path), resolver, scriptsDir,
            manifest, modifications, report, out var unmapped);
        MergeUnmapped(report, unmapped, Path.GetFileName(path));
        if (converted == 0)
        {
            return;
        }

        Backup(path);
        File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        report.Converted += converted;
    }

    private static int ConvertProfileTimePoints(JsonObject root, string profileName, Func<Guid, string?> resolver,
        string scriptsDir, ScriptManifest manifest, List<TimePointModification> modifications,
        LegacyImportReport report, out List<string> unmapped)
    {
        unmapped = new List<string>();
        if (root["TimeLayouts"] is not JsonObject timeLayouts)
        {
            return 0;
        }

        var converted = 0;
        foreach (var pair in timeLayouts)
        {
            if (pair.Value is not JsonObject layout || layout["Layouts"] is not JsonArray items)
            {
                continue;
            }

            if (!Guid.TryParse(pair.Key, out var layoutId))
            {
                continue;
            }

            for (var i = 0; i < items.Count; i++)
            {
                if (items[i] is not JsonObject item || !IsActionTimePoint(item)
                    || item["ActionSet"] is not JsonObject actionSetNode)
                {
                    continue;
                }

                var actionSet = actionSetNode.Deserialize<DtoActionSet>(JsonOptions) ?? new DtoActionSet();
                var doLines = new List<string>();
                var undoLines = new List<string>();
                var needAwait = false;
                var localWarnings = new List<string>();
                CollectActions(actionSet, doLines, undoLines, ref needAwait, unmapped, localWarnings);
                report.Warnings.AddRange(localWarnings);

                var fileName = UniqueTimePointName(scriptsDir, profileName, converted + 1);
                var script = BuildTimePointScript(profileName, item, needAwait, doLines, unmapped);
                File.WriteAllText(Path.Combine(scriptsDir, fileName), script);
                EnsureManifestEntry(manifest, fileName, $"{profileName} 时间点", true);

                item["Script"] = fileName;
                item.Remove("ActionSet");
                modifications.Add(new TimePointModification(layoutId, i, fileName));
                converted++;
            }
        }

        return converted;
    }

    private static bool IsActionTimePoint(JsonObject item)
    {
        return item["TimeType"] is JsonValue value && value.TryGetValue<int>(out var timeType)
               && timeType == TimeTypeAction;
    }

    private static string BuildTimePointScript(string profileName, JsonObject item, bool needAwait,
        List<string> doLines, List<string> unmapped)
    {
        var time = item["StartTime"] is JsonValue v && v.TryGetValue<string>(out var t) ? t : "";
        var sb = new StringBuilder();
        sb.AppendLine($"// 由 LegacyIsland 时间点行动导入：{profileName} {time}");
        sb.AppendLine($"{(needAwait ? "async " : "")}function trigger(ctx) {{");
        foreach (var line in doLines)
        {
            sb.AppendLine("    " + line);
        }

        sb.AppendLine("}");
        AppendUnmappedComments(sb, unmapped);
        return sb.ToString();
    }

    // ============================ 条件（设置/组件布局）============================

    private void ImportSettings(LegacyImportReport report)
    {
        var path = Path.Combine(App.AppRootFolderPath, "Settings.json");
        if (!File.Exists(path))
        {
            return;
        }

        JsonObject? root;
        try
        {
            root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
        }
        catch (Exception ex)
        {
            report.Warnings.Add($"无法解析设置：{ex.Message}");
            return;
        }

        var node = (root?["HiedRules"] ?? root?["HidingRules"]) as JsonObject;
        if (node == null || !node.ContainsKey("Groups"))
        {
            return;
        }

        var unmapped = new List<string>();
        var expr = MapRuleset(node.Deserialize<DtoRuleset>(JsonOptions) ?? new DtoRuleset(),
            SubjectResolver(_profileService.Profile), unmapped);
        MergeUnmapped(report, unmapped, "Settings.HiedRules");

        Backup(path);
        _settingsService.Settings.HideCondition = expr;
        _settingsService.SaveSettings("旧配置导入");
        report.Converted++;
    }

    private void ImportComponentLayouts(LegacyImportReport report, Func<Guid, string?> resolver)
    {
        var dir = Path.Combine(App.AppConfigPath, "ComponentLayouts");
        if (!Directory.Exists(dir))
        {
            return;
        }

        var currentName = _settingsService.Settings.CurrentComponentConfig;
        var currentPath = Path.Combine(dir, currentName + ".json");
        if (File.Exists(currentPath))
        {
            ImportCurrentComponentLayout(currentPath, report, resolver);
        }

        foreach (var path in Directory.GetFiles(dir, "*.json"))
        {
            if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(currentPath), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            TransformConditionsFile(path, report, resolver);
        }
    }

    private void ImportCurrentComponentLayout(string path, LegacyImportReport report, Func<Guid, string?> resolver)
    {
        JsonObject? root;
        try
        {
            root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
        }
        catch (Exception ex)
        {
            report.Warnings.Add($"无法解析组件布局 {Path.GetFileName(path)}：{ex.Message}");
            return;
        }

        if (root?["Lines"] is not JsonArray lines)
        {
            return;
        }

        var memLines = _componentsService.CurrentComponents.Lines;
        var unmapped = new List<string>();
        var changed = false;
        for (var i = 0; i < lines.Count && i < memLines.Count; i++)
        {
            if (lines[i] is not JsonObject line)
            {
                continue;
            }

            if (TryMapConditionNode(line["HidingRules"], resolver, unmapped, out var lineExpr))
            {
                memLines[i].HideCondition = lineExpr;
                changed = true;
            }

            if (line["Children"] is not JsonArray children || memLines[i].Children == null)
            {
                continue;
            }

            var memChildren = memLines[i].Children!;
            for (var j = 0; j < children.Count && j < memChildren.Count; j++)
            {
                if (children[j] is JsonObject child
                    && TryMapConditionNode(child["HidingRules"], resolver, unmapped, out var childExpr))
                {
                    memChildren[j].HideCondition = childExpr;
                    changed = true;
                }
            }
        }

        MergeUnmapped(report, unmapped, Path.GetFileName(path));
        if (!changed)
        {
            return;
        }

        Backup(path);
        _componentsService.SaveConfig();
        report.Converted++;
    }

    private static void TransformConditionsFile(string path, LegacyImportReport report, Func<Guid, string?> resolver)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            report.Warnings.Add($"无法解析组件布局 {Path.GetFileName(path)}：{ex.Message}");
            return;
        }

        if (root == null)
        {
            return;
        }

        var unmapped = new List<string>();
        if (!TransformConditionsNode(root, resolver, unmapped))
        {
            return;
        }

        Backup(path);
        File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        MergeUnmapped(report, unmapped, Path.GetFileName(path));
        report.Converted++;
    }

    private static bool TransformConditionsNode(JsonNode node, Func<Guid, string?> resolver, List<string> unmapped)
    {
        var changed = false;
        if (node is JsonObject obj)
        {
            foreach (var key in obj.Select(x => x.Key).ToList())
            {
                var value = obj[key];
                switch (key)
                {
                    case "HiedRules":
                    case "HidingRules":
                        if (TryMapConditionNode(value, resolver, unmapped, out var hideExpr))
                        {
                            obj.Remove(key);
                            obj["HideCondition"] = hideExpr;
                            changed = true;
                        }
                        else if (value != null && TransformConditionsNode(value, resolver, unmapped))
                        {
                            changed = true;
                        }

                        break;
                    case "PauseRule":
                        if (TryMapConditionNode(value, resolver, unmapped, out var pauseExpr))
                        {
                            obj.Remove(key);
                            obj["PauseCondition"] = pauseExpr;
                            changed = true;
                        }

                        break;
                    case "StopRule":
                        if (TryMapConditionNode(value, resolver, unmapped, out var stopExpr))
                        {
                            obj.Remove(key);
                            obj["StopCondition"] = stopExpr;
                            changed = true;
                        }

                        break;
                    default:
                        if (value != null && TransformConditionsNode(value, resolver, unmapped))
                        {
                            changed = true;
                        }

                        break;
                }
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array.Where(x => x != null))
            {
                if (TransformConditionsNode(item!, resolver, unmapped))
                {
                    changed = true;
                }
            }
        }

        return changed;
    }

    private static bool TryMapConditionNode(JsonNode? node, Func<Guid, string?> resolver, List<string> unmapped,
        out string expression)
    {
        expression = "";
        if (node is not JsonObject obj || !obj.ContainsKey("Groups"))
        {
            return false;
        }

        expression = MapRuleset(obj.Deserialize<DtoRuleset>(JsonOptions) ?? new DtoRuleset(), resolver, unmapped);
        return true;
    }

    // ============================ 条件映射 ============================

    /// <summary>
    /// 将一个旧规则集转换为 JS 布尔表达式，镜像 <c>RulesetService</c> 的判定逻辑。
    /// </summary>
    private static string MapRuleset(DtoRuleset? ruleset, Func<Guid, string?> subjectName, List<string> unmapped)
    {
        if (ruleset == null)
        {
            return "false";
        }

        var groupExpressions = new List<string>();
        foreach (var group in ruleset.Groups ?? new List<DtoRuleGroup>())
        {
            if (!group.IsEnabled)
            {
                continue;
            }

            if (group.Rules == null || group.Rules.All(r => string.IsNullOrEmpty(r.Id)))
            {
                continue;
            }

            var ruleExpressions = new List<string>();
            foreach (var rule in group.Rules)
            {
                if (string.IsNullOrEmpty(rule.Id))
                {
                    continue;
                }

                var leaf = MapRule(rule, subjectName, unmapped);
                if (rule.IsReversed)
                {
                    leaf = $"!({leaf})";
                }

                ruleExpressions.Add(leaf);
            }

            if (ruleExpressions.Count == 0)
            {
                continue;
            }

            var separator = group.Mode == ModeAnd ? " && " : " || ";
            var groupExpression = "(" + string.Join(separator, ruleExpressions) + ")";
            if (group.IsReversed)
            {
                groupExpression = $"!({groupExpression})";
            }

            groupExpressions.Add(groupExpression);
        }

        if (groupExpressions.Count == 0)
        {
            return "false";
        }

        var rulesetSeparator = ruleset.Mode == ModeAnd ? " && " : " || ";
        var expression = groupExpressions.Count == 1
            ? groupExpressions[0]
            : "(" + string.Join(rulesetSeparator, groupExpressions) + ")";
        if (ruleset.IsReversed)
        {
            expression = $"!({expression})";
        }

        return expression;
    }

    private static string MapRule(DtoRule rule, Func<Guid, string?> subjectName, List<string> unmapped)
    {
        switch (rule.Id)
        {
            case "classisland.test.true":
                return "true";
            case "classisland.test.false":
                return "false";
            case "classisland.windows.className":
                return MapStringMatch("window.className", Get<DtoStringMatch>(rule.Settings));
            case "classisland.windows.text":
                return MapStringMatch("window.title", Get<DtoStringMatch>(rule.Settings));
            case "classisland.windows.processName":
                return MapStringMatch("window.processName", Get<DtoStringMatch>(rule.Settings));
            case "classisland.windows.status":
            {
                var s = Get<DtoWindowStatus>(rule.Settings) ?? new DtoWindowStatus();
                var state = s.State switch
                {
                    1 => "maximized",
                    2 => "minimized",
                    3 => "fullscreen",
                    _ => "normal"
                };
                return $"window.state === '{state}'";
            }
            case "classisland.lessons.currentSubject":
                return MapSubject("lessons.currentSubjectId", Get<DtoCurrentSubject>(rule.Settings), subjectName);
            case "classisland.lessons.nextSubject":
                return MapSubject("lessons.nextSubjectId", Get<DtoCurrentSubject>(rule.Settings), subjectName);
            case "classisland.lessons.previousSubject":
                return MapSubject("lessons.previousSubjectId", Get<DtoCurrentSubject>(rule.Settings), subjectName);
            case "classisland.lessons.timeState":
            {
                var s = Get<DtoTimeState>(rule.Settings) ?? new DtoTimeState();
                return s.State == 0
                    ? "(lessons.currentState === 'None' || lessons.currentState === 'AfterSchool')"
                    : $"lessons.currentState === '{TimeStateName(s.State)}'";
            }
            case "classisland.weather.currentWeather":
            {
                var s = Get<DtoCurrentWeather>(rule.Settings) ?? new DtoCurrentWeather();
                return $"(weather.isRefreshed && weather.current === {Js(s.WeatherId.ToString(CultureInfo.InvariantCulture))})";
            }
            case "classisland.weather.hasWeatherAlert":
            {
                var s = Get<DtoStringMatch>(rule.Settings) ?? new DtoStringMatch();
                var test = s.UseRegex ? $"/{EscapeRegex(s.Text)}/.test(a.title)" : $"a.title === {Js(s.Text)}";
                return $"(weather.isRefreshed && weather.alerts.some(a => {test}))";
            }
            case "classisland.weather.rainTime":
            {
                var s = Get<DtoRainTime>(rule.Settings) ?? new DtoRainTime();
                return $"weather.rainIn({Num(s.RainTimeMinutes)}, {{ remaining: {Bool(s.IsRemainingTime)} }})";
            }
            default:
                if (!unmapped.Contains(rule.Id))
                {
                    unmapped.Add(rule.Id);
                }

                return "false";
        }
    }

    private static string MapStringMatch(string accessor, DtoStringMatch? settings)
    {
        settings ??= new DtoStringMatch();
        return settings.UseRegex
            ? $"/{EscapeRegex(settings.Text)}/.test({accessor})"
            : $"{accessor} === {Js(settings.Text)}";
    }

    private static string MapSubject(string accessor, DtoCurrentSubject? settings, Func<Guid, string?> subjectName)
    {
        settings ??= new DtoCurrentSubject();
        var expression = $"{accessor} === {Js(settings.SubjectId.ToString())}";
        var name = subjectName(settings.SubjectId);
        if (!string.IsNullOrEmpty(name))
        {
            expression += $" /* 科目：{name} */";
        }

        return expression;
    }

    private static string BuildFunction(string name, bool async, List<string> lines)
    {
        var sb = new StringBuilder();
        sb.Append("    ").Append(name).Append(": ").Append(async ? "async " : "").AppendLine("function () {");
        foreach (var line in lines)
        {
            sb.Append("        ").AppendLine(line);
        }

        sb.Append("    }");
        return sb.ToString();
    }

    private static void AppendUnmappedComments(StringBuilder sb, List<string> unmapped)
    {
        if (unmapped.Count == 0)
        {
            return;
        }

        sb.AppendLine();
        foreach (var id in unmapped)
        {
            sb.AppendLine($"// [未映射] {id}");
        }
    }

    // ============================ 小工具 ============================

    private static Func<Guid, string?> SubjectResolver(Profile profile)
    {
        var map = profile.Subjects
            .Where(x => x.Value != null && !string.IsNullOrWhiteSpace(x.Value.Name))
            .ToDictionary(x => x.Key, x => x.Value.Name);
        return id => map.TryGetValue(id, out var name) ? name : null;
    }

    private static Func<Guid, string?>? SubjectResolverFromJson(JsonObject profile)
    {
        if (profile["Subjects"] is not JsonObject subjects)
        {
            return null;
        }

        var map = new Dictionary<Guid, string>();
        foreach (var pair in subjects)
        {
            if (Guid.TryParse(pair.Key, out var id)
                && pair.Value is JsonObject subject
                && subject["Name"] is JsonValue nameValue
                && nameValue.TryGetValue<string>(out var name)
                && !string.IsNullOrWhiteSpace(name))
            {
                map[id] = name;
            }
        }

        return id => map.TryGetValue(id, out var name) ? name : null;
    }

    private static void MergeUnmapped(LegacyImportReport report, List<string> local, string scope)
    {
        foreach (var id in local)
        {
            report.Unmapped.Add($"{scope}：{id}");
        }
    }

    private static void EnsureManifestEntry(ScriptManifest manifest, string file, string name, bool enabled)
    {
        var entry = manifest.Scripts.FirstOrDefault(x =>
            string.Equals(x.File, file, StringComparison.OrdinalIgnoreCase));
        if (entry == null)
        {
            entry = new ScriptManifestEntry { File = file, Order = manifest.Scripts.Count };
            manifest.Scripts.Add(entry);
        }

        if (!string.IsNullOrWhiteSpace(name))
        {
            entry.Name = name;
        }

        entry.Enabled = enabled;
    }

    private static string UniqueTimePointName(string scriptsDir, string profileName, int start)
    {
        var safe = SanitizeFileName(profileName);
        var index = start;
        string path;
        do
        {
            path = Path.Combine(scriptsDir, $"{safe}-timepoint-{index}.js");
            index++;
        } while (File.Exists(path));

        return Path.GetFileName(path);
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            sb.Append(invalid.Contains(c) ? '_' : c);
        }

        return sb.Length == 0 ? "profile" : sb.ToString();
    }

    private static int MaxExistingIndex(string dir, string prefix, string extension)
    {
        var max = 0;
        foreach (var path in Directory.GetFiles(dir, prefix + "*" + extension))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            if (name.Length > prefix.Length && int.TryParse(name.Substring(prefix.Length), out var value))
            {
                max = Math.Max(max, value);
            }
        }

        return max;
    }

    private static void Backup(string path)
    {
        try
        {
            var bak = path + ".bak";
            if (!File.Exists(bak))
            {
                File.Copy(path, bak);
            }
        }
        catch
        {
            // 备份失败不阻止导入。
        }
    }

    private static T? Get<T>(JsonElement element) where T : class
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        try
        {
            return element.Deserialize<T>(JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static string TimeStateName(int state)
        => state >= 0 && state < TimeStateNames.Length ? TimeStateNames[state] : state.ToString(CultureInfo.InvariantCulture);

    private static string PreStateName(int state)
        => state switch
        {
            1 => "OnClass",
            3 => "Breaking",
            _ => TimeStateName(state)
        };

    private static string Js(string? value) => JsonSerializer.Serialize(value ?? "", JsStringOptions);

    private static string Num(double value) => value.ToString("0.############", CultureInfo.InvariantCulture);

    private static string Bool(bool value) => value ? "true" : "false";

    private static string EscapeRegex(string value)
        => value.Replace("\\", "\\\\").Replace("/", "\\/").Replace("\r", "\\r").Replace("\n", "\\n");

    private sealed class TimePointModification
    {
        public TimePointModification(Guid timeLayoutId, int itemIndex, string fileName)
        {
            TimeLayoutId = timeLayoutId;
            ItemIndex = itemIndex;
            FileName = fileName;
        }

        public Guid TimeLayoutId { get; }
        public int ItemIndex { get; }
        public string FileName { get; }
    }

    // ============================ 私有 DTO（镜像旧 JSON）============================

    private sealed class DtoRuleset
    {
        public int Mode { get; set; }
        public bool IsReversed { get; set; }
        public List<DtoRuleGroup>? Groups { get; set; }
    }

    private sealed class DtoRuleGroup
    {
        public List<DtoRule>? Rules { get; set; }
        public int Mode { get; set; } = ModeAnd;
        public bool IsReversed { get; set; }
        public bool IsEnabled { get; set; } = true;
    }

    private sealed class DtoRule
    {
        public bool IsReversed { get; set; }
        public string Id { get; set; } = "";
        public JsonElement Settings { get; set; }
    }

    private sealed class DtoWorkflow
    {
        public DtoRuleset? Ruleset { get; set; }
        public DtoActionSet? ActionSet { get; set; }
        public List<DtoTrigger>? Triggers { get; set; }
        public bool IsConditionEnabled { get; set; }
    }

    private sealed class DtoActionSet
    {
        public bool IsEnabled { get; set; } = true;
        public string Name { get; set; } = "";
        public string Guid { get; set; } = "";
        public List<DtoAction>? Actions { get; set; }
        public bool IsRevertEnabled { get; set; } = true;
    }

    private sealed class DtoAction
    {
        public string Id { get; set; } = "";
        public JsonElement Settings { get; set; }
        public bool IsEnabled { get; set; } = true;
    }

    private sealed class DtoTrigger
    {
        public string Id { get; set; } = "";
        public JsonElement Settings { get; set; }
    }

    private sealed class DtoStringMatch
    {
        public string Text { get; set; } = "";
        public bool UseRegex { get; set; }
    }

    private sealed class DtoWindowStatus
    {
        public int State { get; set; } = 1;
    }

    private sealed class DtoCurrentSubject
    {
        public Guid SubjectId { get; set; }
    }

    private sealed class DtoTimeState
    {
        public int State { get; set; }
    }

    private sealed class DtoCurrentWeather
    {
        public int WeatherId { get; set; }
    }

    private sealed class DtoRainTime
    {
        public double RainTimeMinutes { get; set; } = 60;
        public bool IsRemainingTime { get; set; }
    }

    private sealed class DtoRun
    {
        public string Value { get; set; } = "";
        public string Args { get; set; } = "";
    }

    private sealed class DtoSleep
    {
        public double Value { get; set; }
    }

    private sealed class DtoIntValue
    {
        public int Value { get; set; }
    }

    private sealed class DtoStringValue
    {
        public string Value { get; set; } = "";
    }

    private sealed class DtoBoolValue
    {
        public bool Value { get; set; }
    }

    private sealed class DtoNotification
    {
        public string Content { get; set; } = "";
        public string Mask { get; set; } = "";
        public bool IsContentSpeechEnabled { get; set; } = true;
        public bool IsMaskSpeechEnabled { get; set; } = true;
        public bool IsSoundEffectEnabled { get; set; } = true;
        public bool IsTopmostEnabled { get; set; } = true;
        public string CustomSoundEffectPath { get; set; } = "";
        public double MaskDurationSeconds { get; set; } = 5;
        public double ContentDurationSeconds { get; set; } = 10;
        public bool IsEffectEnabled { get; set; } = true;
        public bool IsAdvancedSettingsEnabled { get; set; }
    }

    private sealed class DtoWeatherNotify
    {
        public int NotificationKind { get; set; }
    }

    private sealed class DtoSignal
    {
        public string SignalName { get; set; } = "";
        public bool IsRevert { get; set; }
    }

    private sealed class DtoCron
    {
        public string CronExpression { get; set; } = "* * * * *";
    }

    private sealed class DtoUri
    {
        public string UriSuffix { get; set; } = "";
    }

    private sealed class DtoPreTimePoint
    {
        public int TargetState { get; set; } = 1;
        public double TimeSeconds { get; set; } = 60;
    }

    // ============================ 清单读写桥接 ============================

    private static class ConfigureFileHelperBridge
    {
        public static ScriptManifest Load(string scriptsDir)
            => ConfigureFileHelper.LoadConfig<ScriptManifest>(
                Path.Combine(scriptsDir, "scripts.json"));

        public static void Save(string scriptsDir, ScriptManifest manifest)
            => ConfigureFileHelper.SaveConfig(
                Path.Combine(scriptsDir, "scripts.json"), manifest, true);
    }

    // ============================ 自检 ============================

    /// <summary>
    /// 映射逻辑自检。在 DEBUG 构建点击导入时自动运行，也可在调试器中手动调用
    /// <c>ClassIsland.Services.Scripting.LegacyImporter.MappingSelfTest()</c>。
    /// </summary>
    public static void MappingSelfTest()
    {
        // And 嵌套
        var andSet = new DtoRuleset
        {
            Mode = ModeAnd,
            Groups = new List<DtoRuleGroup>
            {
                new()
                {
                    Mode = ModeAnd,
                    Rules = new List<DtoRule>
                    {
                        new() { Id = "classisland.test.true" },
                        new() { Id = "classisland.test.false" }
                    }
                }
            }
        };
        Debug.Assert(MapRuleset(andSet, _ => null, new List<string>()) == "(true && false)");

        // Or 分组
        var orSet = new DtoRuleset
        {
            Mode = 0,
            Groups = new List<DtoRuleGroup>
            {
                new()
                {
                    Mode = 0,
                    Rules = new List<DtoRule>
                    {
                        new() { Id = "classisland.test.true" },
                        new() { Id = "classisland.test.false" }
                    }
                }
            }
        };
        Debug.Assert(MapRuleset(orSet, _ => null, new List<string>()) == "(true || false)");

        // 规则反转
        var reversedRule = new DtoRuleset
        {
            Mode = ModeAnd,
            Groups = new List<DtoRuleGroup>
            {
                new()
                {
                    Mode = ModeAnd,
                    Rules = new List<DtoRule> { new() { Id = "classisland.test.true", IsReversed = true } }
                }
            }
        };
        Debug.Assert(MapRuleset(reversedRule, _ => null, new List<string>()) == "(!(true))");

        // 分组反转
        var reversedGroup = new DtoRuleset
        {
            Mode = ModeAnd,
            Groups = new List<DtoRuleGroup>
            {
                new()
                {
                    Mode = ModeAnd,
                    IsReversed = true,
                    Rules = new List<DtoRule> { new() { Id = "classisland.test.true" } }
                }
            }
        };
        Debug.Assert(MapRuleset(reversedGroup, _ => null, new List<string>()) == "!((true))");

        // 空分组跳过 -> false
        var emptyGroup = new DtoRuleset
        {
            Mode = ModeAnd,
            Groups = new List<DtoRuleGroup>
            {
                new() { Rules = new List<DtoRule> { new() { Id = "" } } }
            }
        };
        Debug.Assert(MapRuleset(emptyGroup, _ => null, new List<string>()) == "false");

        // 未知规则上报
        var unknownUnmapped = new List<string>();
        var unknown = new DtoRuleset
        {
            Mode = ModeAnd,
            Groups = new List<DtoRuleGroup>
            {
                new() { Rules = new List<DtoRule> { new() { Id = "example.unknown" } } }
            }
        };
        Debug.Assert(MapRuleset(unknown, _ => null, unknownUnmapped) == "(false)");
        Debug.Assert(unknownUnmapped.Contains("example.unknown"));

        // 完整工作流脚本
        var workflow = new DtoWorkflow
        {
            IsConditionEnabled = true,
            Ruleset = new DtoRuleset
            {
                Mode = ModeAnd,
                Groups = new List<DtoRuleGroup>
                {
                    new() { Mode = ModeAnd, Rules = new List<DtoRule> { new() { Id = "classisland.test.true" } } }
                }
            },
            Triggers = new List<DtoTrigger> { new() { Id = "classisland.lifetime.startup" } },
            ActionSet = new DtoActionSet
            {
                Name = "自检",
                IsRevertEnabled = true,
                Actions = new List<DtoAction>
                {
                    new() { Id = "classisland.os.run", Settings = El("{\"Value\":\"notepad.exe\",\"Args\":\"\"}") },
                    new() { Id = "classisland.settings.theme", Settings = El("{\"Value\":2}") },
                    new() { Id = "classisland.action.sleep", Settings = El("{\"Value\":3}") }
                }
            }
        };
        var script = BuildWorkflowScript(workflow, _ => null, 0, new List<string>(), new List<string>());
        Debug.Assert(script != null);
        Debug.Assert(script!.Contains("on.startup(__workflow_0);"));
        Debug.Assert(script.Contains("when: () => ((true))"));
        Debug.Assert(script.Contains("do: async function () {"));
        Debug.Assert(script.Contains("run(\"notepad.exe\", \"\");"));
        Debug.Assert(script.Contains("clearTheme();"));
        Debug.Assert(script.Contains("await sleep(3);"));
    }

    private static JsonElement El(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
