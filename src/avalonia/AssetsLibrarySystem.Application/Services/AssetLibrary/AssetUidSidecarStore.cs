using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;

namespace AssetsLibrarySystem.Application.Services.AssetLibrary;

internal sealed class AssetUidSidecarStore
{
    private const string SystemCreatedBy = "AssetsLibrarySystem";

    private JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private ConcurrentDictionary<string, CachedUidSidecar> Cache { get; } = new(StringComparer.OrdinalIgnoreCase);

    public static string GenerateUid() => $"asset_{Guid.NewGuid():N}";

    public static string GetSidecarPath(string assetPath) => assetPath + ".uid";

    public string? Read(string sidecarPath, FileInfo sidecarInfo, out bool hasSidecar)
    {
        hasSidecar = sidecarInfo.Exists;
        if (!hasSidecar)
        {
            return null;
        }

        if (Cache.TryGetValue(sidecarPath, out var cached) &&
            cached.Length == sidecarInfo.Length &&
            cached.LastWriteTimeUtc == sidecarInfo.LastWriteTimeUtc)
        {
            return cached.Uid;
        }

        try
        {
            using var stream = sidecarInfo.Open(FileMode.Open, FileAccess.Read, FileShare.Read);
            var document = JsonSerializer.Deserialize<UidSidecarDocument>(stream, JsonOptions);
            var uid = string.IsNullOrWhiteSpace(document?.Uid) ? null : document.Uid.Trim();
            CacheSidecar(sidecarPath, sidecarInfo, uid);
            return uid;
        }
        catch
        {
            return null;
        }
    }

    public void Write(string sidecarPath, string assetUid)
    {
        var document = new UidSidecarDocument
        {
            Uid = assetUid,
            Version = 1,
            CreatedBy = SystemCreatedBy
        };

        File.WriteAllText(sidecarPath, JsonSerializer.Serialize(document, JsonOptions));
        CacheSidecar(sidecarPath, new FileInfo(sidecarPath), assetUid);
    }

    private void CacheSidecar(string sidecarPath, FileInfo sidecarInfo, string? uid)
    {
        Cache[sidecarPath] = new CachedUidSidecar(sidecarInfo.Length, sidecarInfo.LastWriteTimeUtc, uid);
    }

    private sealed class UidSidecarDocument
    {
        public string Uid { get; set; } = string.Empty;
        public int Version { get; set; }
        public string CreatedBy { get; set; } = string.Empty;
    }

    private sealed record CachedUidSidecar(long Length, DateTime LastWriteTimeUtc, string? Uid);
}
