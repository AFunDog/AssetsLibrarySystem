using System;
using System.Linq;
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

        // 3. 构建角度 DTO（prompt 由 Python 端根据模板 + angles 构建）
        var angleDtos = profile.Angles
            .Select(a => new AngleDefinitionDto(a.Key, a.Label, a.Prompt, a.MaxLength))
            .ToArray();

        // 4. 发送请求（不传 system_prompt，让 Python 端根据模板构建）
        var slicing = profile.Slicing;
        var request = new BackendModelGenerateRequest(
            AssetFormat: asset.AssetType,
            AssetPath: asset.LocalPath,
            Prompt: string.IsNullOrWhiteSpace(prompt) ? null : prompt.Trim(),
            SystemPrompt: null,  // Python 端根据模板 + angles 构建
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
            MetadataStatus: "ready",
            Subtype: subtype);

        await Store.SaveAsync(document, ct);

        Log.Information("素材描述已写入 SQLite: {DatabasePath}, subtype={Subtype}, angles={AngleCount}",
            Store.DatabasePath, subtype, angleDtos.Length);

        return document;
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