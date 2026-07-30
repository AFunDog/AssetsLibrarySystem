using System;
using System.Collections.Concurrent;

namespace AssetsLibrarySystem.Application.Services.AssetSearch;

/// <summary>
/// 按 embedding 模型键缓存 HNSW 管理器，避免每次检索 new + 整图反序列化。
/// </summary>
internal static class LocalHnswSearchIndexManagerCache
{
    private static readonly ConcurrentDictionary<string, LocalHnswSearchIndexManager> ByModel =
        new(StringComparer.Ordinal);

    public static LocalHnswSearchIndexManager Get(string embeddingModelKey)
    {
        if (string.IsNullOrWhiteSpace(embeddingModelKey))
        {
            embeddingModelKey = "default";
        }

        return ByModel.GetOrAdd(embeddingModelKey, key => new LocalHnswSearchIndexManager(key));
    }
}
