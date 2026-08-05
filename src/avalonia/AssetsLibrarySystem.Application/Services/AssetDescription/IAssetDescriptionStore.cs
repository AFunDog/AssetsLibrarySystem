using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AssetsLibrarySystem.Application.Models;

namespace AssetsLibrarySystem.Application.Services.AssetDescription;

public interface IAssetDescriptionStore
{
    string DatabasePath { get; }

    Task SaveAsync(AssetDescriptionDocument document, CancellationToken ct = default);

    Task<AssetDescriptionDocument?> TryGetAsync(long assetId, CancellationToken ct = default);

    /// <summary>批量读取素材描述（一次查询，避免扫描结果展示时的 N+1 查询）</summary>
    Task<IReadOnlyDictionary<long, AssetDescriptionDocument>> GetDescriptionsAsync(
        IReadOnlyCollection<long> assetIds,
        CancellationToken ct = default);

    Task<AssetDescriptionDocument?> TryGetForAssetAsync(ManagedAssetRecord asset, CancellationToken ct = default);

    Task<bool> DeleteAsync(long assetId, CancellationToken ct = default);

    /// <summary>手动更新素材描述文本，标记向量为过期状态</summary>
    Task UpdateDescriptionAsync(long assetId, string newDescription, CancellationToken ct = default);

    /// <summary>
    /// 记录一次描述调用的 token/费用流水（累计持久化，用于花费审计）。
    /// document.TokenUsage 为 null（如骨架分割落库）时忽略，不写流水。
    /// </summary>
    Task AppendTokenUsageAsync(AssetDescriptionDocument document, CancellationToken ct = default);

    /// <summary>
    /// 记录一次 API 调用（描述/向量化/检索查询向量化/重排）的 token/费用流水。
    /// assetId 为 null 表示与单个素材无关的调用（如检索）。
    /// </summary>
    Task AppendApiUsageAsync(
        string operation,
        string mode,
        string model,
        long? assetId,
        string assetName,
        string assetType,
        string? query,
        int inputTokens,
        int outputTokens,
        int totalTokens,
        double? estimatedCostCny,
        CancellationToken ct = default);

    /// <summary>
    /// 获取 token/费用累计统计与最近流水。
    /// assetId 为空且 libraryId 为空时汇总全部素材；libraryId 非空时按素材库过滤。
    /// </summary>
    Task<AssetTokenUsageSummary> GetTokenUsageSummaryAsync(
        long? assetId = null,
        long? libraryId = null,
        int limit = 20,
        CancellationToken ct = default);
}
