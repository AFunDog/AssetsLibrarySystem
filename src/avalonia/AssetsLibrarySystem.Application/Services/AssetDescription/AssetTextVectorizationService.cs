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

    public async Task<VectorizationResult> VectorizeAsync(
        AssetDescriptionDocument document,
        string backendBaseUrl,
        string provider,
        string model,
        int embeddingDimensions,
        string embeddingModelKey,
        IReadOnlyDictionary<string, AssetDescriptionVectorDocument>? existingByAngle = null,
        CancellationToken ct = default)
    {
        var isClip = string.Equals(document.AssetType, "视频剪辑", StringComparison.Ordinal);

        // 剪辑素材：片段×角度（angle_type = segN_角度）；普通素材：顶层角度
        var vectorSources = new List<(string AngleType, string Text)>();
        if (isClip)
        {
            foreach (var segment in StructuredDescriptionHelper.EnumerateSegmentAngleTexts(document.Description))
            {
                vectorSources.Add((SegmentAngleType.Build(segment.SegmentIndex, segment.AngleType), segment.Text));
            }
        }
        else
        {
            foreach (var segment in StructuredDescriptionHelper.ExtractSegments(document.Description))
            {
                vectorSources.Add((segment.NormalizedAngleType, segment.NormalizedText));
            }
        }

        var vectorDocuments = new List<AssetDescriptionVectorDocument>(vectorSources.Count);
        var totalTokens = 0;
        var requestedDimensions = string.Equals(provider, "dashscope", StringComparison.OrdinalIgnoreCase)
            ? embeddingDimensions
            : (int?)null;
        foreach (var (angleType, text) in vectorSources)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var fingerprint = ComputeFingerprint(text);

            // 片段级增量：文本未变化的向量直接复用，跳过 embedding 调用
            if (existingByAngle is not null
                && existingByAngle.TryGetValue(angleType, out var existing)
                && string.Equals(existing.SourceFingerprint, fingerprint, StringComparison.Ordinal))
            {
                vectorDocuments.Add(existing);
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
                Description: text,
                GeneratedAt: document.GeneratedAt);

            var vectorResponse = await BackendSearchClient.IndexAsync(backendBaseUrl, request, ct).ConfigureAwait(false);
            if (vectorResponse.TokenUsage is { } tokenUsage)
            {
                totalTokens += tokenUsage;
            }

            vectorDocuments.Add(new AssetDescriptionVectorDocument(
                AssetId: document.AssetId,
                AssetUid: vectorResponse.AssetId,
                AngleType: angleType,
                EmbeddingModel: embeddingModelKey,
                VectorDim: vectorResponse.VectorDim,
                Vector: JsonSerializer.Deserialize<float[]>(vectorResponse.Vector.GetRawText()) ?? [],
                VectorizedAt: DateTimeOffset.UtcNow,
                ContentHash: document.ContentHash,
                SourceFingerprint: fingerprint));
        }

        if (vectorDocuments.Count == 0)
        {
            throw new InvalidOperationException("当前描述中没有可向量化的有效角度文本。");
        }

        return new VectorizationResult(vectorDocuments, totalTokens);
    }

    /// <summary>向量化结果：文档列表 + 本次实际 API 调用的累计 token（跳过的未变片段不计入）。</summary>
    public sealed record VectorizationResult(
        IReadOnlyList<AssetDescriptionVectorDocument> Documents,
        int TotalTokens);

    /// <summary>计算剪辑素材期望的片段角度指纹（angleType → 文本指纹），用于增量判断</summary>
    public static IReadOnlyDictionary<string, string> ComputeExpectedFingerprints(AssetDescriptionDocument document)
    {
        var result = new Dictionary<string, string>();
        foreach (var segment in StructuredDescriptionHelper.EnumerateSegmentAngleTexts(document.Description))
        {
            if (string.IsNullOrWhiteSpace(segment.Text))
            {
                continue;
            }

            result[SegmentAngleType.Build(segment.SegmentIndex, segment.AngleType)] = ComputeFingerprint(segment.Text);
        }

        return result;
    }

    private static string ComputeFingerprint(string text)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes)[..16];
    }
}
