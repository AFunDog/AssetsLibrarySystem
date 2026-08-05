using AssetsLibrarySystem.Application.Services.AssetSearch;
using System.Text.Json;
using Xunit;

namespace AssetsLibrarySystem.Application.Tests;

public sealed class LocalHnswSearchIndexManagerTests : IDisposable
{
    private string TempDirectory { get; } = Path.Combine(Path.GetTempPath(), "assets-library-hnsw-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void RebuildAndReload_PreservesSearchBehavior()
    {
        Directory.CreateDirectory(TempDirectory);
        var indexPath = Path.Combine(TempDirectory, "vectors.hnsw");
        var metadataPath = Path.Combine(TempDirectory, "vectors.meta.json");
        var vectors = new[]
        {
            new[] { 1f, 0f, 0f },
            new[] { 0f, 1f, 0f },
            new[] { 0f, 0f, 1f },
        };
        var orderedKeys = new[] { "asset-1::全面", "asset-2::全面", "asset-3::全面" };
        var state = new LocalVectorIndexState(vectors.Length, DateTimeOffset.UtcNow.ToString("O"));

        var builder = new LocalHnswSearchIndexManager(indexPath, metadataPath);
        builder.Rebuild(vectors, orderedKeys, state);

        Assert.True(File.Exists(indexPath));
        Assert.True(File.Exists(metadataPath));

        var reloaded = new LocalHnswSearchIndexManager(indexPath, metadataPath);
        reloaded.EnsureCurrent(vectors, orderedKeys, state);

        var results = reloaded.Search(new[] { 1f, 0f, 0f }, 2);

        Assert.NotEmpty(results);
        Assert.Contains(results, item => item.Index == 0);
        Assert.All(results, item => Assert.True(item.Similarity >= 0f));
    }

    [Fact]
    public void Search_ThrowsForDimensionMismatch()
    {
        Directory.CreateDirectory(TempDirectory);
        var indexPath = Path.Combine(TempDirectory, "vectors.hnsw");
        var metadataPath = Path.Combine(TempDirectory, "vectors.meta.json");
        var vectors = new[]
        {
            new[] { 1f, 0f, 0f },
            new[] { 0f, 1f, 0f },
        };
        var orderedKeys = new[] { "asset-1::全面", "asset-2::全面" };
        var state = new LocalVectorIndexState(vectors.Length, DateTimeOffset.UtcNow.ToString("O"));
        var manager = new LocalHnswSearchIndexManager(indexPath, metadataPath);
        manager.Rebuild(vectors, orderedKeys, state);

        var action = () => manager.Search(new[] { 1f, 0f }, 1);

        Assert.Throws<InvalidOperationException>(action);
    }

    [Fact]
    public void EnsureCurrent_RebuildsWhenOrderedKeysChange()
    {
        Directory.CreateDirectory(TempDirectory);
        var indexPath = Path.Combine(TempDirectory, "vectors.hnsw");
        var metadataPath = Path.Combine(TempDirectory, "vectors.meta.json");
        var vectors = new[]
        {
            new[] { 1f, 0f, 0f },
            new[] { 0f, 1f, 0f },
            new[] { 0f, 0f, 1f },
        };
        var initialKeys = new[] { "asset-1::全面", "asset-2::全面", "asset-3::全面" };
        var reorderedVectors = new[]
        {
            vectors[2],
            vectors[0],
            vectors[1],
        };
        var reorderedKeys = new[] { "asset-3::全面", "asset-1::全面", "asset-2::全面" };
        var state = new LocalVectorIndexState(vectors.Length, "2026-06-10T12:00:00.0000000+00:00");

        var manager = new LocalHnswSearchIndexManager(indexPath, metadataPath);
        manager.Rebuild(vectors, initialKeys, state);

        var reloaded = new LocalHnswSearchIndexManager(indexPath, metadataPath);
        reloaded.EnsureCurrent(reorderedVectors, reorderedKeys, state);

        var results = reloaded.Search(new[] { 0f, 0f, 1f }, 1);

        Assert.Single(results);
        Assert.Equal(0, results[0].Index);

        using var metadata = JsonDocument.Parse(File.ReadAllText(metadataPath));
        var metadataKeys = metadata.RootElement.GetProperty("OrderedKeys")
            .EnumerateArray()
            .Select(item => item.GetString())
            .ToArray();
        Assert.Equal(reorderedKeys, metadataKeys);
    }

    [Fact]
    public void EnsureCurrent_ThrowsWhenVectorAndKeyCountsDoNotMatch()
    {
        Directory.CreateDirectory(TempDirectory);
        var indexPath = Path.Combine(TempDirectory, "vectors.hnsw");
        var metadataPath = Path.Combine(TempDirectory, "vectors.meta.json");
        var vectors = new[]
        {
            new[] { 1f, 0f, 0f },
            new[] { 0f, 1f, 0f },
        };
        var orderedKeys = new[] { "asset-1::全面" };
        var state = new LocalVectorIndexState(vectors.Length, DateTimeOffset.UtcNow.ToString("O"));
        var manager = new LocalHnswSearchIndexManager(indexPath, metadataPath);

        var action = () => manager.EnsureCurrent(vectors, orderedKeys, state);

        Assert.Throws<InvalidOperationException>(action);
    }

    [Fact]
    public void EnsureCurrent_RebuildsWhenContentChangedWithinSameTimestamp()
    {
        // 回归：批量向量化可能在同一 tick 写入（LatestUpdatedAt 相同）而内容已变化，
        // 仅靠键与时间戳判断会命中过期内存图；指纹不一致时应重建索引。
        Directory.CreateDirectory(TempDirectory);
        var indexPath = Path.Combine(TempDirectory, "vectors.hnsw");
        var metadataPath = Path.Combine(TempDirectory, "vectors.meta.json");
        var keys = new[] { "asset-1::整体", "asset-2::整体" };
        var state = new LocalVectorIndexState(2, "2026-06-10T12:00:00.0000000+00:00");

        var before = new[]
        {
            new[] { 1f, 0f, 0f },
            new[] { 0f, 1f, 0f },
        };
        var after = new[]
        {
            new[] { 0f, 1f, 0f },
            new[] { 1f, 0f, 0f },
        };

        var manager = new LocalHnswSearchIndexManager(indexPath, metadataPath);
        manager.EnsureCurrent(before, keys, state);

        // 同实例、同键、同时间戳，但向量内容已变化
        manager.EnsureCurrent(after, keys, state);

        // 查询应命中内容变化后的第一个向量（0,1,0）
        var results = manager.Search(new[] { 0f, 1f, 0f }, 1);

        Assert.Single(results);
        Assert.Equal(0, results[0].Index);
    }

    public void Dispose()
    {
        if (Directory.Exists(TempDirectory))
        {
            Directory.Delete(TempDirectory, recursive: true);
        }
    }
}
