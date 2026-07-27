using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using YamlDotNet.Serialization;

namespace AssetsLibrarySystem.Application.Models;

/// <summary>
/// 单个角度的定义
/// </summary>
public sealed record AngleDefinition(
    string Key,
    string Label,
    string Prompt,
    int MaxLength = 120);

/// <summary>
/// 视频切片配置
/// </summary>
public sealed record VideoSlicingConfig(
    bool Enabled = true,
    double SliceThresholdSeconds = 60.0,
    int MinSceneLength = 15,
    double AdaptiveThreshold = 3.0);

/// <summary>
/// 提示词模板（从 YAML 的 prompt_template 加载）
/// </summary>
public sealed record PromptTemplate(
    string Role,
    string Intro,
    IReadOnlyList<string> Rules,
    string Note);

/// <summary>
/// 子类型的角度配置
/// </summary>
public sealed record SubtypeProfile(
    string AssetType,
    string Subtype,
    string Label,
    IReadOnlyList<AngleDefinition> Angles,
    VideoSlicingConfig? Slicing = null);

/// <summary>
/// 角度配置管理器。
/// 加载 angle_profiles.yaml 并提供子类型 → 角度组合的查询。
/// </summary>
public sealed class AngleProfileManager
{
    private readonly Dictionary<string, Dictionary<string, SubtypeProfile>> _profiles;
    private readonly Dictionary<string, AngleDefinition> _angleDefs;
    private readonly PromptTemplate _promptTemplate;

    public AngleProfileManager(string yamlPath)
    {
        var yaml = File.ReadAllText(yamlPath);
        var deserializer = new DeserializerBuilder().Build();
        var root = deserializer.Deserialize<Dictionary<object, object>>(yaml);

        _promptTemplate = LoadPromptTemplate(root);
        _angleDefs = LoadAngleDefinitions(root);
        _profiles = LoadProfiles(root);
    }

    public PromptTemplate PromptTemplate => _promptTemplate;

    /// <summary>根据素材类型和子类型获取角度配置</summary>
    public SubtypeProfile GetProfile(string assetType, string? subtype)
    {
        if (!_profiles.TryGetValue(assetType, out var subtypes))
        {
            return new SubtypeProfile(assetType, "默认", "通用素材", new[] { GetDefaultAngle() });
        }

        if (subtype is not null && subtypes.TryGetValue(subtype, out var profile))
            return profile;

        if (subtypes.TryGetValue("默认", out var defaultProfile))
            return defaultProfile;

        return new SubtypeProfile(assetType, "默认", "通用素材", new[] { GetDefaultAngle() });
    }

    /// <summary>获取某类型的所有子类型配置</summary>
    public IReadOnlyList<SubtypeProfile> GetProfiles(string assetType)
    {
        if (!_profiles.TryGetValue(assetType, out var subtypes))
            return [];

        return subtypes.Values.ToList();
    }

    /// <summary>获取所有可用的素材类型</summary>
    public IReadOnlyList<string> GetAssetTypes() => _profiles.Keys.ToList();

    private static AngleDefinition GetDefaultAngle() =>
        new("整体", "整体", "用一句话概括该素材", 120);

    private static PromptTemplate LoadPromptTemplate(Dictionary<object, object> root)
    {
        var raw = root.GetDict("prompt_template");
        if (raw is null)
            return GetDefaultPromptTemplate();

        return new PromptTemplate(
            Role: raw.GetString("role") ?? GetDefaultPromptTemplate().Role,
            Intro: raw.GetString("intro") ?? GetDefaultPromptTemplate().Intro,
            Rules: raw.GetList("rules")?.Select(r => r?.ToString() ?? "").Where(s => !string.IsNullOrEmpty(s)).ToList()
                   ?? GetDefaultPromptTemplate().Rules,
            Note: raw.GetString("note") ?? GetDefaultPromptTemplate().Note);
    }

    private static PromptTemplate GetDefaultPromptTemplate()
    {
        return new PromptTemplate(
            Role: "你是{asset_type}素材结构化描述助手。",
            Intro: "请根据输入的素材内容、格式信息和绝对路径，输出严格合法的 JSON 对象。",
            Rules:
            [
                "只描述当前素材内容本身，不做文件管理、使用建议、版权判断或目录推断。",
                "只写素材中能明确看到或听到的内容，不得臆造。",
                "不要把文件名、路径、目录名当作素材内容，除非素材本身支持。",
                "只能输出 JSON，不要输出 Markdown、代码块、解释或额外文本。",
                "每个字段的值必须是对象 {\"text\": ..., \"tags\": [...]}，不能是普通字符串。",
                "如果某个字段没有合适的标签，tags 请使用空数组 []，不要省略该字段。",
                "每个 text 用中文，不超过对应字段的最大字数。",
                "tags 是简短中文标签数组，适合筛选和展示，避免重复和长句。",
                "JSON 字符串必须使用双引号，不能有注释或尾随逗号。",
            ],
            Note: "注意：必须严格按照以上示例的嵌套格式输出。");
    }

    /// <summary>根据角度配置和模板构建系统提示词</summary>
    public string BuildSystemPrompt(string assetType, IReadOnlyList<AngleDefinition> angles)
    {
        var role = _promptTemplate.Role.Replace("{asset_type}", assetType);
        var angleKeys = string.Join(", ", angles.Select(a => $"\"{a.Key}\""));
        var lines = new List<string>
        {
            role,
            _promptTemplate.Intro,
            "",
            "输出要求：",
        };
        lines.AddRange(_promptTemplate.Rules.Select(r => $"- {r}"));
        lines.Insert(lines.Count - 1, $"- JSON 必须包含且只包含以下字段： {angleKeys}");

        lines.Add("");
        lines.Add("字段含义：");
        foreach (var angle in angles)
        {
            lines.Add($"- \"{angle.Key}\"：{angle.Prompt}（不超过 {angle.MaxLength} 字）");
        }

        lines.Add("");
        lines.Add("输出格式示例：");
        lines.Add("{");
        for (int i = 0; i < angles.Count; i++)
        {
            var comma = i < angles.Count - 1 ? "," : "";
            lines.Add($"  \"{angles[i].Key}\": {{ \"text\": \"...\", \"tags\": [\"...\"] }}{comma}");
        }
        lines.Add("}");
        lines.Add("");
        lines.Add(_promptTemplate.Note);

        return string.Join("\n", lines);
    }

    private static Dictionary<string, AngleDefinition> LoadAngleDefinitions(
        Dictionary<object, object> root)
    {
        var defs = new Dictionary<string, AngleDefinition>(StringComparer.Ordinal);
        if (!root.TryGetValue("angle_definitions", out var raw) || raw is not Dictionary<object, object> rawDefs)
            return defs;

        foreach (var (key, value) in rawDefs)
        {
            var keyStr = key?.ToString() ?? "";
            if (value is Dictionary<object, object> obj)
            {
                defs[keyStr] = new AngleDefinition(
                    Key: keyStr,
                    Label: obj.GetString("label") ?? keyStr,
                    Prompt: obj.GetString("prompt") ?? "",
                    MaxLength: obj.GetInt("max_length") ?? 120);
            }
        }

        return defs;
    }

    private Dictionary<string, Dictionary<string, SubtypeProfile>> LoadProfiles(
        Dictionary<object, object> root)
    {
        var result = new Dictionary<string, Dictionary<string, SubtypeProfile>>(StringComparer.Ordinal);

        // 跳过 angle_definitions 键，处理其他顶层键
        foreach (var (key, value) in root)
        {
            var assetType = key?.ToString() ?? "";
            if (assetType == "angle_definitions" || value is not Dictionary<object, object> subtypes)
                continue;

            var subtypeDict = new Dictionary<string, SubtypeProfile>(StringComparer.Ordinal);
            foreach (var (subKey, subValue) in subtypes)
            {
                var subtypeName = subKey?.ToString() ?? "";
                if (subValue is not Dictionary<object, object> subObj)
                    continue;

                var label = subObj.GetString("label") ?? subtypeName;
                var angles = ResolveAngles(subObj.GetList("angles"));
                var slicing = ParseSlicingConfig(subObj.GetDict("slicing"));

                subtypeDict[subtypeName] = new SubtypeProfile(
                    AssetType: assetType,
                    Subtype: subtypeName,
                    Label: label,
                    Angles: angles,
                    Slicing: slicing);
            }

            result[assetType] = subtypeDict;
        }

        return result;
    }

    private IReadOnlyList<AngleDefinition> ResolveAngles(List<object>? rawAngles)
    {
        if (rawAngles is null || rawAngles.Count == 0)
            return [GetDefaultAngle()];

        var result = new List<AngleDefinition>();
        foreach (var item in rawAngles)
        {
            if (item is string key)
            {
                // 简写：从 angle_definitions 查找
                if (_angleDefs.TryGetValue(key, out var def))
                    result.Add(def);
                else
                    result.Add(new AngleDefinition(key, key, "", 120));
            }
            else if (item is Dictionary<object, object> dict)
            {
                // 完整定义（内联）
                var angleKey = dict.GetString("key") ?? "";
                var label = dict.GetString("label") ?? angleKey;
                var prompt = dict.GetString("prompt") ?? "";
                var maxLength = dict.GetInt("max_length") ?? 120;
                result.Add(new AngleDefinition(angleKey, label, prompt, maxLength));
            }
        }

        return result;
    }

    private static VideoSlicingConfig? ParseSlicingConfig(Dictionary<object, object>? raw)
    {
        if (raw is null)
            return null;

        return new VideoSlicingConfig(
            Enabled: raw.GetBool("enabled") ?? true,
            SliceThresholdSeconds: raw.GetDouble("slice_threshold") ?? 60.0,
            MinSceneLength: raw.GetInt("min_scene_len") ?? 15,
            AdaptiveThreshold: raw.GetDouble("adaptive_threshold") ?? 3.0);
    }
}

file static class DictExtensions
{
    public static string? GetString(this Dictionary<object, object> dict, string key)
    {
        if (dict.TryGetValue(key, out var value) && value is string s)
            return s;
        return null;
    }

    public static int? GetInt(this Dictionary<object, object> dict, string key)
    {
        if (dict.TryGetValue(key, out var value))
        {
            // YamlDotNet 16.x 可能将纯数字值解析为字符串，使用 Convert 统一处理
            try { return Convert.ToInt32(value); } catch { }
        }
        return null;
    }

    public static List<object>? GetList(this Dictionary<object, object> dict, string key)
    {
        if (dict.TryGetValue(key, out var value) && value is List<object> list)
            return list;
        return null;
    }

    public static bool? GetBool(this Dictionary<object, object> dict, string key)
    {
        if (dict.TryGetValue(key, out var value))
        {
            try { return Convert.ToBoolean(value); } catch { }
        }
        return null;
    }

    public static double? GetDouble(this Dictionary<object, object> dict, string key)
    {
        if (dict.TryGetValue(key, out var value))
        {
            try { return Convert.ToDouble(value); } catch { }
        }
        return null;
    }

    public static Dictionary<object, object>? GetDict(this Dictionary<object, object> dict, string key)
    {
        if (dict.TryGetValue(key, out var value) && value is Dictionary<object, object> d)
            return d;
        return null;
    }
}