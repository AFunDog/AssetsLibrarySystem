using System;
using System.Collections.Concurrent;

namespace AssetsLibrarySystem.Application.Services.AssetSearch;

/// <summary>
/// 按 embedding 模型键缓存 HNSW 管理器，避免每次检索 new + 整图反序列化。
/// </summary>
internal static class LocalHnswSearchIndexManagerCache
{
    private const int MaxCachedModels = 4;
    private static readonly ConcurrentDictionary<string, LocalHnswSearchIndexManager> ByModel =
        new(StringComparer.Ordinal);

    public static LocalHnswSearchIndexManager Get(string embeddingModelKey)
    {
        if (string.IsNullOrWhiteSpace(embeddingModelKey))
        {
            embeddingModelKey = "default";
        }

        // 模型切换后旧 manager（含整张内存图）驻留：达到上限时整体清空释放，
        // 避免无上限累积造成内存泄漏（下次访问时重建即可）
        if (ByModel.Count >= MaxCachedModels && !ByModel.ContainsKey(embeddingModelKey))
        {
            ByModel.Clear();
        }

        return ByModel.GetOrAdd(embeddingModelKey, key => new LocalHnswSearchIndexManager(key));
    }
}
