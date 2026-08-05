using System;
using System.Collections.Generic;
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

        ct.ThrowIfCancellationRequested(); // 取消检查：取消时不发起 LLM 调用
        var backendResponse = await BackendModelClient.GenerateAsync(backendBaseUrl, request, ct).ConfigureAwait(false);

        // 取消检查点：LLM 已返回但用户已点取消时，不保存结果（与 DescribeClipAsync 行为一致）
        ct.ThrowIfCancellationRequested();

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

    public async Task<AssetDescriptionDocument> DescribeClipAsync(
        ManagedAssetRecord asset,
        string backendBaseUrl,
        double? rangeStart,
        double? rangeEnd,
        CancellationToken ct = default,
        Action<int>? progress = null)
    {
        var subtype = asset.Subtype;
        if (string.IsNullOrWhiteSpace(subtype))
        {
            subtype = SubtypeDetector.DetectSubtype(asset) ?? "默认";
        }

        var profile = AngleProfileManager.GetProfile(asset.AssetType, subtype);
        var angleDtos = profile.Angles
            .Select(a => new AngleDefinitionDto(a.Key, a.Label, a.Prompt, a.MaxLength))
            .ToArray();
        var slicing = profile.Slicing;

        // 1. 读取现有描述，确保有分割结果（骨架先落库）
        var existing = await Store.TryGetAsync(asset.DatabaseId, ct).ConfigureAwait(false);
        var existingJson = existing?.Description;
        var mergedJson = await EnsureSkeletonAsync(
            asset, backendBaseUrl, angleDtos, slicing, existingJson, rangeStart, rangeEnd, subtype, ct, progress).ConfigureAwait(false);

        // 2. 计算缺失（未描述）片段
        var missing = StructuredDescriptionHelper.GetMissingSegmentRanges(mergedJson, rangeStart, rangeEnd);
        if (missing.Count == 0)
        {
            Log.Information("剪辑素材片段已全部描述，跳过: assetId={AssetId}, name={Name}", asset.DatabaseId, asset.Name);
            return existing ?? BuildDocument(asset, backendBaseUrl, mergedJson, "slicing", null, subtype);
        }

        // 3. 按缺失片段时间点描述（跳过场景检测）
        ct.ThrowIfCancellationRequested(); // 取消检查：取消时不发起 LLM 调用
        var request = new BackendModelGenerateRequest(
            AssetFormat: asset.AssetType,
            AssetPath: asset.LocalPath,
            Prompt: null,
            SystemPrompt: null,
            MockResponse: false,
            Subtype: subtype,
            Angles: angleDtos,
            EnableSlicing: slicing?.Enabled == true,
            SliceThreshold: slicing?.SliceThresholdSeconds ?? 60.0,
            MinSceneLen: slicing?.MinSceneLength ?? 15,
            AdaptiveThreshold: slicing?.AdaptiveThreshold ?? 3.0,
            ExistingSegments: missing.Select(segment => new SegmentRangeDto(segment.Start, segment.End)).ToArray());

        var backendResponse = await BackendModelClient.GenerateAsync(backendBaseUrl, request, ct).ConfigureAwait(false);

        // 4. 合并回写（保留范围外旧片段与已描述片段）
        ct.ThrowIfCancellationRequested(); // 取消检查：不保存取消后的结果
        var finalJson = StructuredDescriptionHelper.MergeClipSegments(mergedJson, backendResponse.OutputText);
        var document = BuildDocument(asset, backendBaseUrl, finalJson, backendResponse.Mode, MapTokenUsage(backendResponse.TokenUsage), subtype);
        await Store.SaveAsync(document, ct);

        Log.Information("剪辑素材片段描述完成: assetId={AssetId}, name={Name}, describedSegments={SegmentCount}",
            asset.DatabaseId, asset.Name, missing.Count);

        return document;
    }

    public async Task<ClipSplitResult> SplitOnlyAsync(
        ManagedAssetRecord asset,
        string backendBaseUrl,
        double? rangeStart,
        double? rangeEnd,
        CancellationToken ct = default,
        Action<int>? progress = null)
    {
        var subtype = asset.Subtype;
        if (string.IsNullOrWhiteSpace(subtype))
        {
            subtype = SubtypeDetector.DetectSubtype(asset) ?? "默认";
        }

        var profile = AngleProfileManager.GetProfile(asset.AssetType, subtype);
        var angleDtos = profile.Angles
            .Select(a => new AngleDefinitionDto(a.Key, a.Label, a.Prompt, a.MaxLength))
            .ToArray();
        var slicing = profile.Slicing;

        var existing = await Store.TryGetAsync(asset.DatabaseId, ct).ConfigureAwait(false);
        var existingJson = existing?.Description;

        // 已有分割结果且（无范围或范围已覆盖）→ 幂等跳过
        var alreadySplit = StructuredDescriptionHelper.GetSegmentCount(existingJson) > 0
            && (rangeStart is null || StructuredDescriptionHelper.IsRangeCovered(existingJson, rangeStart, rangeEnd));
        if (alreadySplit)
        {
            Log.Information("剪辑素材已有分割结果，跳过: assetId={AssetId}, name={Name}",
                asset.DatabaseId, asset.Name);
            var document = existing ?? BuildDocument(asset, backendBaseUrl, existingJson ?? string.Empty, "slicing", null, subtype);
            return new ClipSplitResult(document, StructuredDescriptionHelper.GetSegmentCount(existingJson), AlreadySplit: true);
        }

        var mergedJson = await EnsureSkeletonAsync(
            asset, backendBaseUrl, angleDtos, slicing, existingJson, rangeStart, rangeEnd, subtype, ct, progress).ConfigureAwait(false);
        var skeletonDocument = BuildDocument(asset, backendBaseUrl, mergedJson, "slicing", null, subtype);
        return new ClipSplitResult(
            skeletonDocument,
            StructuredDescriptionHelper.GetSegmentCount(mergedJson),
            AlreadySplit: false);
    }

    private async Task<string> EnsureSkeletonAsync(
        ManagedAssetRecord asset,
        string backendBaseUrl,
        AngleDefinitionDto[] angleDtos,
        VideoSlicingConfig? slicing,
        string? existingJson,
        double? rangeStart,
        double? rangeEnd,
        string subtype,
        CancellationToken ct,
        Action<int>? progress = null)
    {
        // 已有片段且（无范围或范围已覆盖）→ 直接复用
        if (StructuredDescriptionHelper.GetSegmentCount(existingJson) > 0
            && (rangeStart is null || StructuredDescriptionHelper.IsRangeCovered(existingJson, rangeStart, rangeEnd)))
        {
            return existingJson!;
        }

        // 先只分割：保存片段时间点（骨架），描述失败时时间点仍在
        var slicingRequest = new BackendModelGenerateRequest(
            AssetFormat: asset.AssetType,
            AssetPath: asset.LocalPath,
            Prompt: null,
            SystemPrompt: null,
            MockResponse: false,
            Subtype: subtype,
            Angles: angleDtos,
            EnableSlicing: slicing?.Enabled == true,
            SliceThreshold: slicing?.SliceThresholdSeconds ?? 60.0,
            MinSceneLen: slicing?.MinSceneLength ?? 15,
            AdaptiveThreshold: slicing?.AdaptiveThreshold ?? 3.0,
            SlicingOnly: true,
            RangeStart: rangeStart,
            RangeEnd: rangeEnd);

        var skeletonResponse = await BackendModelClient.GenerateAsync(backendBaseUrl, slicingRequest, ct, progress).ConfigureAwait(false);

        // mock 模式（未配置 API Key）下后端不执行真实场景检测，分割结果无意义
        if (string.Equals(skeletonResponse.Mode, "mock", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "后端处于 mock 模式（未配置 API Key），无法执行真实场景分割。请在 src/backend/.env 或环境变量中配置 DASHSCOPE_API_KEY 后重试。");
        }

        var merged = StructuredDescriptionHelper.MergeClipSegments(existingJson, skeletonResponse.OutputText);

        // 分割结果先落库
        var skeletonDocument = BuildDocument(asset, backendBaseUrl, merged, skeletonResponse.Mode, null, subtype);
        await Store.SaveAsync(skeletonDocument, ct).ConfigureAwait(false);

        Log.Information("剪辑素材分割结果已落库: assetId={AssetId}, segments={SegmentCount}",
            asset.DatabaseId, StructuredDescriptionHelper.GetSegmentCount(merged));

        return merged;
    }

    private static AssetDescriptionDocument BuildDocument(
        ManagedAssetRecord asset,
        string backendBaseUrl,
        string description,
        string mode,
        AssetDescriptionTokenUsage? tokenUsage,
        string subtype) =>
        new(
            AssetId: asset.DatabaseId,
            AssetUid: asset.AssetUid,
            AssetName: asset.Name,
            AssetType: asset.AssetType,
            CurrentPath: asset.CurrentPath,
            Description: description,
            BackendEndpoint: backendBaseUrl,
            Mode: mode,
            GeneratedAt: DateTimeOffset.UtcNow,
            TokenUsage: tokenUsage,
            Prompt: null,
            SystemPrompt: null,
            ContentHash: asset.ContentHash,
            MetadataStatus: "ready",
            Subtype: subtype);

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
            usage.PromptTokensDetails,
            usage.EstimatedCostCny);
}