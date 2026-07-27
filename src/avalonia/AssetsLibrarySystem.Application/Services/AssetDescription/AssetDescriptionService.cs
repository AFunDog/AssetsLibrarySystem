using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AssetsLibrarySystem.Application.Models;
using AssetsLibrarySystem.Application.Services.BackendApi;
using Serilog;

namespace AssetsLibrarySystem.Application.Services.AssetDescription;

public sealed class AssetDescriptionService : IAssetDescriptionService
{
    private IAssetDescriptionStore Store { get; }
    private IBackendModelClient BackendModelClient { get; }
    private AngleProfileManager AngleProfileManager { get; }
    private ISubtypeDetector SubtypeDetector { get; }

    public AssetDescriptionService(
        IAssetDescriptionStore store,
        IBackendModelClient backendModelClient,
        AngleProfileManager angleProfileManager,
        ISubtypeDetector subtypeDetector)
    {
        Store = store;
        BackendModelClient = backendModelClient;
        AngleProfileManager = angleProfileManager;
        SubtypeDetector = subtypeDetector;
    }

    public async Task<AssetDescriptionDocument> DescribeAsync(
        ManagedAssetRecord asset,
        string backendBaseUrl,
        string? prompt,
        string? systemPrompt,
        CancellationToken ct = default)
    {
        // 1. 检测子类型
        var subtype = asset.Subtype;
        if (string.IsNullOrWhiteSpace(subtype))
        {
            subtype = SubtypeDetector.DetectSubtype(asset) ?? "默认";
        }

        // 2. 获取角度配置
        var profile = AngleProfileManager.GetProfile(asset.AssetType, subtype);

        // 3. 构建动态 system prompt（如果用户没有传覆盖）
        var finalSystemPrompt = systemPrompt;
        if (string.IsNullOrWhiteSpace(finalSystemPrompt))
        {
            finalSystemPrompt = BuildDynamicSystemPrompt(asset.AssetType, profile.Angles);
        }

        // 4. 构建角度 DTO
        var angleDtos = profile.Angles
            .Select(a => new AngleDefinitionDto(a.Key, a.Label, a.Prompt, a.MaxLength))
            .ToArray();

        // 5. 发送请求
        var slicing = profile.Slicing;
        var request = new BackendModelGenerateRequest(
            AssetFormat: asset.AssetType,
            AssetPath: asset.LocalPath,
            Prompt: string.IsNullOrWhiteSpace(prompt) ? null : prompt.Trim(),
            SystemPrompt: string.IsNullOrWhiteSpace(finalSystemPrompt) ? null : finalSystemPrompt.Trim(),
            MockResponse: false,
            Subtype: subtype,
            Angles: angleDtos,
            EnableSlicing: slicing?.Enabled == true,
            SliceThreshold: slicing?.SliceThresholdSeconds ?? 60.0,
            MinSceneLen: slicing?.MinSceneLength ?? 15,
            AdaptiveThreshold: slicing?.AdaptiveThreshold ?? 3.0);

        var backendResponse = await BackendModelClient.GenerateAsync(backendBaseUrl, request, ct).ConfigureAwait(false);

        // 6. 构建文档
        var document = new AssetDescriptionDocument(
            AssetId: asset.DatabaseId,
            AssetUid: asset.AssetUid,
            AssetName: asset.Name,
            AssetType: asset.AssetType,
            CurrentPath: asset.CurrentPath,
            Description: backendResponse.OutputText,
            BackendEndpoint: backendBaseUrl,
            Mode: backendResponse.Mode,
            GeneratedAt: DateTimeOffset.UtcNow,
            TokenUsage: MapTokenUsage(backendResponse.TokenUsage),
            Prompt: request.Prompt,
            SystemPrompt: request.SystemPrompt,
            ContentHash: asset.ContentHash,
            MetadataStatus: asset.MetadataStatus,
            Subtype: subtype);

        await Store.SaveAsync(document, ct);

        Log.Information("素材描述已写入 SQLite: {DatabasePath}, subtype={Subtype}, angles={AngleCount}",
            Store.DatabasePath, subtype, angleDtos.Length);

        return document;
    }

    /// <summary>
    /// 根据角度定义动态构建系统提示词。
    /// 这个提示词会传给 Python 后端，让 LLM 按指定角度输出 JSON。
    /// </summary>
    internal static string BuildDynamicSystemPrompt(string assetType, System.Collections.Generic.IReadOnlyList<AngleDefinition> angles)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"你是{assetType}素材结构化描述助手。");
        sb.AppendLine("请根据输入的素材内容、格式信息和绝对路径，输出严格合法的 JSON 对象。");
        sb.AppendLine();
        sb.AppendLine("输出要求：");
        sb.AppendLine("- 只描述当前素材内容本身，不做文件管理、使用建议、版权判断或目录推断。");
        sb.AppendLine("- 只写素材中能明确看到或听到的内容，不得臆造。");
        sb.AppendLine("- 不要把文件名、路径、目录名当作素材内容，除非素材本身支持。");
        sb.AppendLine("- 只能输出 JSON，不要输出 Markdown、代码块、解释或额外文本。");

        var angleKeys = string.Join(", ", angles.Select(a => $"\"{a.Key}\""));
        sb.AppendLine($"- JSON 必须包含且只包含以下字段： {angleKeys}");
        sb.AppendLine("- 每个字段都是对象，包含 \"text\" 和 \"tags\"。");
        sb.AppendLine("- 每个 text 用中文，不超过对应字段的最大字数。");
        sb.AppendLine("- tags 是简短中文标签数组，适合筛选和展示，避免重复和长句。");
        sb.AppendLine("- JSON 字符串必须使用双引号，不能有注释或尾随逗号。");
        sb.AppendLine();
        sb.AppendLine("字段含义：");

        foreach (var angle in angles)
        {
            sb.AppendLine($"- \"{angle.Key}\"：{angle.Prompt}（不超过 {angle.MaxLength} 字）");
        }

        sb.AppendLine();
        sb.AppendLine("输出格式示例：");
        sb.AppendLine("{");
        for (int i = 0; i < angles.Count; i++)
        {
            var comma = i < angles.Count - 1 ? "," : "";
            sb.AppendLine($"  \"{angles[i].Key}\": {{ \"text\": \"...\", \"tags\": [\"...\"] }}{comma}");
        }
        sb.AppendLine("}");

        return sb.ToString();
    }

    private static AssetDescriptionTokenUsage? MapTokenUsage(BackendTokenUsage? usage) =>
        usage is null ? null : new AssetDescriptionTokenUsage(
            usage.InputTokens,
            usage.OutputTokens,
            usage.TotalTokens,
            usage.ImageTokens,
            usage.VideoTokens,
            usage.AudioTokens,
            usage.InputTokensDetails,
            usage.OutputTokensDetails,
            usage.PromptTokensDetails);
}