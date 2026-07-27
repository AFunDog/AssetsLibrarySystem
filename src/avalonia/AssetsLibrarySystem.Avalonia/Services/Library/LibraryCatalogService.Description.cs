using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AssetsLibrarySystem.Application.Models;
using AssetsLibrarySystem.Avalonia.Models;
using Serilog;

namespace AssetsLibrarySystem.Avalonia.Services.Library;

public sealed partial class LibraryCatalogService
{
    private async Task LoadSelectedAssetDescriptionAsync(ManagedAssetRecord? asset)
    {
        if (asset is null)
        {
            ResetSelectedAssetDescription();
            return;
        }

        if (AssetDescriptionStore is null)
        {
            ResetSelectedAssetDescription();
            SelectedAssetDescriptionState = "描述存储未就绪";
            SelectedAssetDescriptionStorePath = "SQLite 存储未就绪";
            SelectedAssetDescriptionText = "当前环境尚未注入描述 SQLite 存储。";
            SelectedAssetAiState = "描述存储未就绪";
            return;
        }

        try
        {
            var document = await AssetDescriptionStore.TryGetForAssetAsync(asset);
            if (document is null)
            {
                ResetSelectedAssetDescription();
                SelectedAssetDescriptionState = "未描述";
                SelectedAssetDescriptionStorePath = AssetDescriptionStore.DatabasePath;
                SelectedAssetDescriptionText = "点击“排入描述任务”后，这里会展示 AI 返回的中文描述。";
                SelectedAssetAiState = "未描述";
                return;
            }

            ApplySelectedAssetDescription(document);
        }
        catch (Exception ex)
        {
            Log.Error(
                ex,
                "读取素材描述失败: assetId={AssetId}, assetUid={AssetUid}, assetName={AssetName}",
                asset.DatabaseId,
                asset.AssetUid,
                asset.Name);
            ResetSelectedAssetDescription();
            SelectedAssetDescriptionState = "描述记录读取失败";
            SelectedAssetDescriptionStorePath = AssetDescriptionStore.DatabasePath;
            SelectedAssetDescriptionText = ex.Message;
            SelectedAssetAiState = "描述读取失败";
        }
    }

    private void ApplySelectedAssetDescription(AssetDescriptionDocument? document)
    {
        if (document is null)
        {
            ResetSelectedAssetDescription();
            return;
        }

        var tokenUsage = document.TokenUsage is null
            ? "未返回 token 用量"
            : FormatTokenUsage(document.TokenUsage);

        SelectedAssetDescriptionState = document.Mode == "live" ? "已描述" : "已描述（占位）";
        SelectedAssetDescriptionStorePath = AssetDescriptionStore?.DatabasePath ?? "SQLite 存储未就绪";
        SelectedAssetDescriptionGeneratedAt = document.GeneratedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        SelectedAssetDescriptionMode = document.Mode;
        SelectedAssetDescriptionTokenUsage = tokenUsage;
        SelectedAssetDescriptionPrompt = string.IsNullOrWhiteSpace(document.Prompt)
            ? "使用配置中的默认 prompt。"
            : document.Prompt;
        SelectedAssetDescriptionSystemPrompt = string.IsNullOrWhiteSpace(document.SystemPrompt)
            ? "使用配置中的默认 system prompt。"
            : document.SystemPrompt;
        SelectedAssetDescriptionText = document.PrimaryDescription;
        SelectedAssetAiState = SelectedAssetDescriptionState;
        SelectedAssetDetail = document.PrimaryDescription;

        // 更新子类型和角度描述
        var subtype = document.Subtype;
        if (string.IsNullOrWhiteSpace(subtype) && SelectedAsset is not null)
            subtype = SelectedAsset.Subtype;
        if (string.IsNullOrWhiteSpace(subtype))
            subtype = "默认";
        SelectedAssetSubtype = subtype;

        if (SelectedAsset is not null)
            RefreshDescriptionAngles(SelectedAsset, document.Description);
    }

    private void ResetSelectedAssetDescription()
    {
        SelectedAssetDescriptionState = "未描述";
        SelectedAssetDescriptionStorePath = "尚未生成描述记录";
        SelectedAssetDescriptionGeneratedAt = "未生成";
        SelectedAssetDescriptionMode = "未生成";
        SelectedAssetDescriptionTokenUsage = "未返回 token 用量";
        SelectedAssetDescriptionPrompt = "尚未生成 prompt。";
        SelectedAssetDescriptionSystemPrompt = "尚未生成 system prompt。";
        SelectedAssetDescriptionText = "当前素材还没有可显示的 AI 描述。";
    }

    private void UpdateTask(string? taskId, string stageText, string? detailText = null)
    {
        if (string.IsNullOrWhiteSpace(taskId))
        {
            return;
        }

        BackgroundTaskService?.UpdateTask(taskId, stageText, detailText);
    }

    private void CompleteTask(string? taskId, string? stageText = null, string? detailText = null)
    {
        if (string.IsNullOrWhiteSpace(taskId))
        {
            return;
        }

        BackgroundTaskService?.CompleteTask(taskId, stageText, detailText);
    }

    private void FailTask(string? taskId, string stageText, string detailText)
    {
        if (string.IsNullOrWhiteSpace(taskId))
        {
            return;
        }

        BackgroundTaskService?.FailTask(taskId, detailText, stageText);
    }

    private static string FormatTokenUsage(AssetDescriptionTokenUsage usage)
    {
        var baseText = $"input={usage.InputTokens}, output={usage.OutputTokens}, total={usage.TotalTokens}";
        return usage.ImageTokens is null && usage.VideoTokens is null && usage.AudioTokens is null
            ? baseText
            : $"{baseText}; image={usage.ImageTokens ?? 0}, video={usage.VideoTokens ?? 0}, audio={usage.AudioTokens ?? 0}";
    }

    private void RefreshDescriptionAngles(ManagedAssetRecord asset, string? descriptionJson)
    {
        SelectedAssetDescriptionAngles.Clear();
        if (string.IsNullOrWhiteSpace(descriptionJson))
            return;

        var subtype = SelectedAssetSubtype;
        if (string.IsNullOrWhiteSpace(subtype))
            subtype = "默认";

        try
        {
            // 解析 JSON 获取每个角度的 tags
            var tagsByAngle = new Dictionary<string, string[]>(StringComparer.Ordinal);
            try
            {
                using var doc = JsonDocument.Parse(descriptionJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        if (prop.Value.ValueKind == JsonValueKind.Object
                            && prop.Value.TryGetProperty("tags", out var tagsEl)
                            && tagsEl.ValueKind == JsonValueKind.Array)
                        {
                            tagsByAngle[prop.Name] = tagsEl.EnumerateArray()
                                .Where(t => t.ValueKind == JsonValueKind.String)
                                .Select(t => t.GetString() ?? "")
                                .Where(t => !string.IsNullOrEmpty(t))
                                .ToArray()!;
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // tags 解析失败不影响主体描述
            }

            var segments = StructuredDescriptionHelper.ExtractSegments(descriptionJson);
            var profile = AngleProfileManager?.GetProfile(asset.AssetType, subtype)
                ?? new AngleProfileManager(ResolveYamlPath()).GetProfile(asset.AssetType, subtype);

            foreach (var segment in segments)
            {
                var angleDef = profile.Angles.FirstOrDefault(
                    a => a.Key == segment.NormalizedAngleType);
                var tags = tagsByAngle.GetValueOrDefault(segment.NormalizedAngleType, []);
                SelectedAssetDescriptionAngles.Add(new AngleDescriptionRecord(
                    AngleKey: segment.NormalizedAngleType,
                    Label: angleDef?.Label ?? segment.NormalizedAngleType,
                    Text: segment.NormalizedText,
                    Tags: tags,
                    MaxLength: angleDef?.MaxLength ?? 120));
            }
        }
        catch (Exception ex)
        {
            Log.Debug("解析角度描述失败: {Error}", ex.Message);
        }
    }

    private static string ResolveYamlPath()
    {
        var baseDir = AppContext.BaseDirectory;
        var yamlPath = System.IO.Path.Combine(baseDir, "angle_profiles.yaml");
        if (System.IO.File.Exists(yamlPath))
            return yamlPath;

        // 回退到源码目录
        var current = new System.IO.DirectoryInfo(baseDir);
        while (current is not null)
        {
            var candidate = System.IO.Path.Combine(
                current.FullName, "src", "avalonia",
                "AssetsLibrarySystem.Application", "angle_profiles.yaml");
            if (System.IO.File.Exists(candidate))
                return candidate;
            current = current.Parent;
        }

        return yamlPath;
    }

    public async Task UpdateSubtypeAsync(string newSubtype)
    {
        if (SelectedAsset is null || AssetDatabase is null)
            return;

        await AssetDatabase.UpdateSubtypeAsync(SelectedAsset.DatabaseId, newSubtype);
        SelectedAssetSubtype = newSubtype;
        RefreshDescriptionAngles(SelectedAsset, SelectedAssetDescriptionText);
        Log.Information("素材子类型已更新: assetId={AssetId}, subtype={Subtype}",
            SelectedAsset.DatabaseId, newSubtype);
    }
}
