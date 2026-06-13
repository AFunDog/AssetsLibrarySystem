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

    Task<bool> NeedsVectorizationAsync(
        long assetId,
        string embeddingModel,
        string? descriptionContentHash = null,
        DateTimeOffset? descriptionGeneratedAt = null,
        CancellationToken ct = default);

    Task MarkAsIndexedAsync(long assetId, CancellationToken ct = default);
}
