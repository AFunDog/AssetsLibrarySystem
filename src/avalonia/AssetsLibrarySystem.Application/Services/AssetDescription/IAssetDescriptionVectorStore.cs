using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AssetsLibrarySystem.Application.Models;

namespace AssetsLibrarySystem.Application.Services.AssetDescription;

public interface IAssetDescriptionVectorStore
{
    string DatabasePath { get; }

    Task ReplaceForAssetAsync(long assetId, string embeddingModel, IReadOnlyList<AssetDescriptionVectorDocument> documents, CancellationToken ct = default);

    Task<IReadOnlyList<AssetDescriptionVectorDocument>> ListByAssetIdAsync(long assetId, CancellationToken ct = default);

    Task<bool> DeleteAsync(long assetId, CancellationToken ct = default);

    /// <summary>删除指定素材在当前模型下的一组角度向量（用于清理剪辑素材已删除片段的残留向量）。</summary>
    Task DeleteAnglesAsync(
        long assetId,
        string embeddingModel,
        IReadOnlyCollection<string> angleTypes,
        CancellationToken ct = default);
    Task<bool> NeedsVectorizationAsync(
        long assetId,
        string embeddingModel,
        string? descriptionContentHash = null,
        DateTimeOffset? descriptionGeneratedAt = null,
        CancellationToken ct = default);

    Task MarkAsIndexedAsync(long assetId, CancellationToken ct = default);
}
