using System;
using System.Threading.Tasks;
using AssetsLibrarySystem.Application.Models;
using AssetsLibrarySystem.Avalonia.Models;

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
            SelectedAssetDescriptionState = "描述存储未注册";
            SelectedAssetDescriptionStorePath = "SQLite 存储未就绪";
            SelectedAssetDescriptionText = "当前环境尚未注入描述 SQLite 存储。";
            return;
        }

        try
        {
            var document = await AssetDescriptionStore.TryGetForAssetAsync(asset);
            if (document is null)
            {
                ResetSelectedAssetDescription();
                SelectedAssetDescriptionState = "当前素材尚未生成 AI 描述";
                SelectedAssetDescriptionStorePath = AssetDescriptionStore.DatabasePath;
                SelectedAssetDescriptionText = "点击“排入描述任务”后，这里会展示 AI 返回的中文描述。";
                return;
            }

            ApplySelectedAssetDescription(document);
        }
        catch (Exception ex)
        {
            ResetSelectedAssetDescription();
            SelectedAssetDescriptionState = "描述记录读取失败";
            SelectedAssetDescriptionStorePath = AssetDescriptionStore.DatabasePath;
            SelectedAssetDescriptionText = ex.Message;
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

        SelectedAssetDescriptionState = document.Mode == "live" ? "已生成" : "已生成（占位）";
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
        SelectedAssetAiState = $"SQLite 已保存 · {tokenUsage}";
        SelectedAssetDetail = document.PrimaryDescription;
    }

    private void ResetSelectedAssetDescription()
    {
        SelectedAssetDescriptionState = "未生成 AI 描述";
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
}
