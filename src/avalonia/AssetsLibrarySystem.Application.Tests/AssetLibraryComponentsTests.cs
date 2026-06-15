using AssetsLibrarySystem.Application.Services.AssetLibrary;
using Xunit;

namespace AssetsLibrarySystem.Application.Tests;

public sealed class AssetLibraryComponentsTests
{
    [Fact]
    public void FileScanner_EnumeratesOnlySupportedAssets()
    {
        using var temp = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(temp.Path, "note.txt"), "text");
        File.WriteAllText(Path.Combine(temp.Path, "ignored.bin"), "binary");
        Directory.CreateDirectory(Path.Combine(temp.Path, "nested"));
        File.WriteAllText(Path.Combine(temp.Path, "nested", "cover.png"), "image");

        var files = new AssetFileScanner().EnumerateSupportedFiles(temp.Path).ToArray();

        Assert.Equal(2, files.Length);
        Assert.Contains(files, path => path.EndsWith("note.txt", StringComparison.Ordinal));
        Assert.Contains(files, path => path.EndsWith("cover.png", StringComparison.Ordinal));
    }

    [Fact]
    public void UidSidecarStore_WritesAndReadsUid()
    {
        using var temp = new TemporaryDirectory();
        var assetPath = Path.Combine(temp.Path, "sample.mp4");
        var sidecarPath = AssetUidSidecarStore.GetSidecarPath(assetPath);
        var store = new AssetUidSidecarStore();
        var uid = AssetUidSidecarStore.GenerateUid();

        store.Write(sidecarPath, uid);
        var result = store.Read(sidecarPath, new FileInfo(sidecarPath), out var hasSidecar);

        Assert.True(hasSidecar);
        Assert.Equal(uid, result);
        Assert.StartsWith("asset_", uid, StringComparison.Ordinal);
    }

    [Fact]
    public void ContentHasher_ReusesUnchangedFileHash()
    {
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "sample.txt");
        File.WriteAllText(path, "stable content");
        var fileInfo = new FileInfo(path);
        var stats = new ScanHashStats();
        var hasher = new AssetContentHasher();

        var first = hasher.GetHash(path, fileInfo, stats);
        var second = hasher.GetHash(path, fileInfo, stats);

        Assert.Equal(first, second);
        Assert.Equal(1, stats.RecomputedHashCount);
        Assert.Equal(1, stats.ReusedHashCount);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, true);
    }
}
