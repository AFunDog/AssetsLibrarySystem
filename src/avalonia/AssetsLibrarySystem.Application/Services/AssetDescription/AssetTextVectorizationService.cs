using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AssetsLibrarySystem.Application.Models;
using AssetsLibrarySystem.Application.Services.BackendApi;

namespace AssetsLibrarySystem.Application.Services.AssetDescription;

public sealed class AssetTextVectorizationService : IAssetTextVectorizationService
{
    private IBackendSearchClient BackendSearchClient { get; }

    public AssetTextVectorizationService(IBackendSearchClient backendSearchClient)
    {
        BackendSearchClient = backendSearchClient;
    }

    public async Task<IReadOnlyList<AssetDescriptionVectorDocument>> VectorizeAsync(
        AssetDescriptionDocument document,
        string backendBaseUrl,
        string provider,
        string model,
        int embeddingDimensions,
        string embeddingModelKey,
        CancellationToken ct = default)
    {
        var segments = StructuredDescriptionHelper.ExtractSegments(document.Description);
        var vectorDocuments = new List<AssetDescriptionVectorDocument>(segments.Count);
        var requestedDimensions = string.Equals(provider, "dashscope", StringComparison.OrdinalIgnoreCase)
            ? embeddingDimensions
            : (int?)null;
        foreach (var segment in segments)
        {
            if (string.IsNullOrWhiteSpace(segment.NormalizedText))
            {
                continue;
            }

            var request = new BackendSearchIndexRequest(
                Provider: provider,
                Model: model,
                EmbeddingDimensions: requestedDimensions,
                AssetId: document.AssetUid,
                AssetName: document.AssetName,
                AssetFormat: document.AssetType,
                AssetPath: document.CurrentPath,
                Description: segment.NormalizedText,
                GeneratedAt: document.GeneratedAt);

            var vectorResponse = await BackendSearchClient.IndexAsync(backendBaseUrl, request, ct).ConfigureAwait(false);

            vectorDocuments.Add(new AssetDescriptionVectorDocument(
                AssetId: document.AssetId,
                AssetUid: vectorResponse.AssetId,
                AngleType: segment.NormalizedAngleType,
                EmbeddingModel: embeddingModelKey,
                VectorDim: vectorResponse.VectorDim,
                Vector: JsonSerializer.Deserialize<float[]>(vectorResponse.Vector.GetRawText()) ?? [],
                VectorizedAt: DateTimeOffset.UtcNow,
                ContentHash: document.ContentHash));
        }

        if (vectorDocuments.Count == 0)
        {
            throw new InvalidOperationException("当前描述中没有可向量化的有效角度文本。");
        }

        return vectorDocuments;
    }
}
