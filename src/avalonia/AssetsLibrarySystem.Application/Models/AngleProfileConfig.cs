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

    public AngleProfileManager(string yamlPath)
    {
        var yaml = File.ReadAllText(yamlPath);
        var deserializer = new DeserializerBuilder().Build();
        var root = deserializer.Deserialize<Dictionary<object, object>>(yaml);

        _angleDefs = LoadAngleDefinitions(root);
        _profiles = LoadProfiles(root);
    }

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