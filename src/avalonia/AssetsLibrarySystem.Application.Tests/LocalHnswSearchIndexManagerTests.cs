using AssetsLibrarySystem.Application.Services.AssetSearch;
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
        var state = new LocalVectorIndexState(vectors.Length, DateTimeOffset.UtcNow.ToString("O"));

        var builder = new LocalHnswSearchIndexManager(indexPath, metadataPath);
        builder.Rebuild(vectors, state);

        Assert.True(File.Exists(indexPath));
        Assert.True(File.Exists(metadataPath));

        var reloaded = new LocalHnswSearchIndexManager(indexPath, metadataPath);
        reloaded.EnsureCurrent(vectors, state);

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
        var state = new LocalVectorIndexState(vectors.Length, DateTimeOffset.UtcNow.ToString("O"));
        var manager = new LocalHnswSearchIndexManager(indexPath, metadataPath);
        manager.Rebuild(vectors, state);

        var action = () => manager.Search(new[] { 1f, 0f }, 1);

        Assert.Throws<InvalidOperationException>(action);
    }

    public void Dispose()
    {
        if (Directory.Exists(TempDirectory))
        {
            Directory.Delete(TempDirectory, recursive: true);
        }
    }
}
