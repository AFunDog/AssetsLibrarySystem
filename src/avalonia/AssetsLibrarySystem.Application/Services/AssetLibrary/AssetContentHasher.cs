using System;
using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Threading;

namespace AssetsLibrarySystem.Application.Services.AssetLibrary;

internal sealed class AssetContentHasher
{
    private ConcurrentDictionary<string, CachedContentHash> Cache { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string GetHash(string path, FileInfo fileInfo, ScanHashStats stats)
    {
        if (Cache.TryGetValue(path, out var cached) &&
            cached.Length == fileInfo.Length &&
            cached.LastWriteTimeUtc == fileInfo.LastWriteTimeUtc)
        {
            stats.IncrementReusedHashCount();
            return cached.Hash;
        }

        stats.IncrementRecomputedHashCount();
        using var stream = File.OpenRead(path);
        var hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        CacheHash(path, fileInfo, hash);
        return hash;
    }

    public void CacheHash(string path, FileInfo fileInfo, string hash)
    {
        Cache[path] = new CachedContentHash(fileInfo.Length, fileInfo.LastWriteTimeUtc, hash);
    }

    public static bool CanReuseStoredHash(long storedFileSize, DateTime storedModifiedTimeUtc, FileInfo fileInfo)
    {
        return storedFileSize == fileInfo.Length && storedModifiedTimeUtc == fileInfo.LastWriteTimeUtc;
    }

    private sealed record CachedContentHash(long Length, DateTime LastWriteTimeUtc, string Hash);
}

internal sealed class ScanHashStats
{
    private int _recomputedHashCount;
    private int _reusedHashCount;
    private int _skippedPersistCount;

    public int RecomputedHashCount => _recomputedHashCount;
    public int ReusedHashCount => _reusedHashCount;
    public int SkippedPersistCount => _skippedPersistCount;

    public void IncrementRecomputedHashCount() => Interlocked.Increment(ref _recomputedHashCount);
    public void IncrementReusedHashCount() => Interlocked.Increment(ref _reusedHashCount);
    public void IncrementSkippedPersistCount() => Interlocked.Increment(ref _skippedPersistCount);
}
