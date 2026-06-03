using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AssetsLibrarySystem.Application.Infrastructure;
using AssetsLibrarySystem.Application.Models;
using AssetsLibrarySystem.Application.Services.Infrastructure;
using Microsoft.Data.Sqlite;
using Serilog;

namespace AssetsLibrarySystem.Application.Services.AssetLibrary;

public sealed class AssetLibraryService : IAssetLibraryService
{
    private const string SystemCreatedBy = "AssetsLibrarySystem";

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".yaml", ".yml", ".csv", ".log", ".xml"
    };

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".avif"
    };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".avi", ".mov", ".webm", ".wmv", ".flv"
    };

    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".wav", ".flac", ".ogg", ".m4a", ".aac", ".wma"
    };

    private JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private string LibraryStorePath { get; }
    private IDatabaseWriteQueue WriteQueue { get; }
    private IAssetDatabase AssetDatabase { get; }
    private ConcurrentDictionary<string, CachedUidSidecar> UidSidecarCache { get; } = new(StringComparer.OrdinalIgnoreCase);
    private ConcurrentDictionary<string, CachedContentHash> ContentHashCache { get; } = new(StringComparer.OrdinalIgnoreCase);

    public AssetLibraryService(IDatabaseWriteQueue writeQueue, IAssetDatabase assetDatabase)
    {
        WriteQueue = writeQueue;
        AssetDatabase = assetDatabase;
        LibraryStorePath = SharedDataPathHelper.GetDataFilePath("libraries.json");
    }

    public async Task<IReadOnlyList<LibraryWorkspace>> GetLibrariesAsync(CancellationToken ct = default)
    {
        var items = await ReadStoreAsync(ct);
        return items
            .Select(item => new LibraryWorkspace(
                item.Id,
                item.Name,
                item.RootPath,
                "尚未扫描，点击“扫描当前素材库”加载素材文件。",
                "已登记目录",
                0))
            .ToList();
    }

    public async Task<LibraryWorkspace> AddLibraryAsync(string folderPath, CancellationToken ct = default)
    {
        var normalizedPath = Path.GetFullPath(folderPath);
        if (!Directory.Exists(normalizedPath))
        {
            throw new DirectoryNotFoundException($"目录不存在：{normalizedPath}");
        }

        var items = await ReadStoreAsync(ct);
        var existing = items.FirstOrDefault(item =>
            string.Equals(item.RootPath, normalizedPath, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            return new LibraryWorkspace(
                existing.Id,
                existing.Name,
                existing.RootPath,
                "目录已存在，可直接扫描。",
                "已登记目录",
                0);
        }

        var created = new LibraryStoreItem
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = BuildLibraryName(normalizedPath, items),
            RootPath = normalizedPath
        };

        items.Add(created);
        await WriteStoreAsync(items, ct);

        return new LibraryWorkspace(
            created.Id,
            created.Name,
            created.RootPath,
            "目录已登记，等待首次扫描。",
            "已登记目录",
            0);
    }

    public Task<IReadOnlyList<ManagedAssetRecord>> ScanLibraryAsync(LibraryWorkspace library, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.Run<IReadOnlyList<ManagedAssetRecord>>(async () => await BuildRecordsForDirectoryAsync(library, ct), ct);
    }

    private async Task<List<ManagedAssetRecord>> BuildRecordsForDirectoryAsync(LibraryWorkspace library, CancellationToken ct)
    {
        await AssetDatabase.EnsureSchemaAsync(ct);

        var stats = new ScanHashStats();
        var scanAt = DateTimeOffset.UtcNow;
        var descriptionTableExists = DescriptionTableExists();
        var records = new ConcurrentBag<ManagedAssetRecord>();
        var paths = EnumerateSupportedFiles(library.RootPath)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        await Parallel.ForEachAsync(paths, new ParallelOptions
        {
            CancellationToken = ct,
            MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount)
        }, async (path, token) =>
        {
            var record = await ImportOrRefreshAssetAsync(library, path, scanAt, descriptionTableExists, stats, token);
            records.Add(record);
        });

        var orderedRecords = records
            .OrderBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Log.Debug(
            "素材库扫描 hash 统计: libraryId={LibraryId}, libraryName={LibraryName}, totalFiles={TotalFiles}, recomputedHashCount={RecomputedHashCount}, reusedHashCount={ReusedHashCount}, skippedPersistCount={SkippedPersistCount}",
            library.Id,
            library.Name,
            orderedRecords.Count,
            stats.RecomputedHashCount,
            stats.ReusedHashCount,
            stats.SkippedPersistCount);

        return orderedRecords;
    }

    private async Task<ManagedAssetRecord> ImportOrRefreshAssetAsync(
        LibraryWorkspace library,
        string fullPath,
        DateTimeOffset scanAt,
        bool descriptionTableExists,
        ScanHashStats stats,
        CancellationToken ct)
    {
        var normalizedPath = Path.GetFullPath(fullPath);
        var relativePath = Path.GetRelativePath(library.RootPath, normalizedPath);
        var fileInfo = new FileInfo(normalizedPath);
        var assetType = ClassifyAssetType(normalizedPath);
        var extension = fileInfo.Extension.TrimStart('.').ToLowerInvariant();
        var sidecarPath = GetUidSidecarPath(normalizedPath);
        var sidecarInfo = new FileInfo(sidecarPath);
        var sidecarUid = TryReadUidSidecar(sidecarPath, sidecarInfo, out var hasUidSidecar);

        AssetDbRecord? assetRecord;
        var currentHash = string.Empty;
        string stage = "已识别";
        string aiState = "身份已确认";
        await using var connection = await AssetDatabase.OpenConnectionAsync(ct);

        if (!string.IsNullOrWhiteSpace(sidecarUid))
        {
            assetRecord = TryGetAssetByUid(connection, sidecarUid!);
            if (assetRecord is null)
            {
                currentHash = GetContentHash(normalizedPath, fileInfo, stats);
                assetRecord = await CreateOrUpdateAssetAsync(
                    library,
                    assetUid: sidecarUid!,
                    assetName: fileInfo.Name,
                    assetType: assetType,
                    currentPath: normalizedPath,
                    contentHash: currentHash,
                    observedHash: currentHash,
                    fileSize: fileInfo.Length,
                    modifiedTimeUtc: fileInfo.LastWriteTimeUtc,
                    status: "ok",
                    scanAt: scanAt);
                await EnsureAssetMetadataAsync(sidecarUid!, "pending", scanAt, ct);
                stage = "已迁入";
                aiState = "待生成描述与向量";
            }
            else if (CanReuseStoredHash(assetRecord, fileInfo))
            {
                currentHash = assetRecord.ContentHash;
                Interlocked.Increment(ref stats.ReusedHashCount);
                CacheContentHash(normalizedPath, fileInfo, currentHash);
                if (IsSameAssetSnapshot(
                        assetRecord,
                        library.Id,
                        fileInfo.Name,
                        assetType,
                        normalizedPath,
                        currentHash,
                        currentHash,
                        fileInfo.Length,
                        fileInfo.LastWriteTimeUtc,
                        "ok"))
                {
                    Interlocked.Increment(ref stats.SkippedPersistCount);
                }
                else
                {
                    assetRecord = await CreateOrUpdateAssetAsync(
                        library,
                        assetUid: assetRecord.AssetUid,
                        assetName: fileInfo.Name,
                        assetType: assetType,
                        currentPath: normalizedPath,
                        contentHash: currentHash,
                        observedHash: currentHash,
                        fileSize: fileInfo.Length,
                        modifiedTimeUtc: fileInfo.LastWriteTimeUtc,
                        status: "ok",
                        scanAt: scanAt,
                        createdAt: assetRecord.CreatedAt,
                        createdBy: assetRecord.CreatedBy,
                        uidVersion: assetRecord.UidVersion);
                    await EnsureAssetMetadataAsync(assetRecord.AssetUid, "ready", scanAt, ct);
                }
                stage = "已同步";
                aiState = "身份已确认";
            }
            else
            {
                currentHash = GetContentHash(normalizedPath, fileInfo, stats);
                if (string.Equals(assetRecord.ContentHash, currentHash, StringComparison.OrdinalIgnoreCase))
                {
                    if (IsSameAssetSnapshot(
                            assetRecord,
                            library.Id,
                            fileInfo.Name,
                            assetType,
                            normalizedPath,
                            assetRecord.ContentHash,
                            currentHash,
                            fileInfo.Length,
                            fileInfo.LastWriteTimeUtc,
                            "ok"))
                    {
                        Interlocked.Increment(ref stats.SkippedPersistCount);
                    }
                    else
                    {
                        assetRecord = await CreateOrUpdateAssetAsync(
                            library,
                            assetUid: assetRecord.AssetUid,
                            assetName: fileInfo.Name,
                            assetType: assetType,
                            currentPath: normalizedPath,
                            contentHash: assetRecord.ContentHash,
                            observedHash: currentHash,
                            fileSize: fileInfo.Length,
                            modifiedTimeUtc: fileInfo.LastWriteTimeUtc,
                            status: "ok",
                            scanAt: scanAt,
                            createdAt: assetRecord.CreatedAt,
                            createdBy: assetRecord.CreatedBy,
                            uidVersion: assetRecord.UidVersion);
                        await EnsureAssetMetadataAsync(assetRecord.AssetUid, "ready", scanAt, ct);
                    }
                    stage = "已同步";
                    aiState = "身份已确认";
                }
                else
                {
                    assetRecord = await CreateOrUpdateAssetAsync(
                        library,
                        assetUid: assetRecord.AssetUid,
                        assetName: fileInfo.Name,
                        assetType: assetType,
                        currentPath: normalizedPath,
                        contentHash: assetRecord.ContentHash,
                        observedHash: currentHash,
                        fileSize: fileInfo.Length,
                        modifiedTimeUtc: fileInfo.LastWriteTimeUtc,
                        status: "changed",
                        scanAt: scanAt,
                        createdAt: assetRecord.CreatedAt,
                        createdBy: assetRecord.CreatedBy,
                        uidVersion: assetRecord.UidVersion);
                    await EnsureAssetMetadataAsync(assetRecord.AssetUid, "changed", scanAt, ct);
                    stage = "内容已变化";
                    aiState = "等待版本处理策略";
                }
            }
        }
        else
        {
            currentHash = GetContentHash(normalizedPath, fileInfo, stats);
            assetRecord = TryGetAssetByContentHash(connection, currentHash);
            if (assetRecord is not null)
            {
                WriteUidSidecar(sidecarPath, assetRecord.AssetUid);
                if (IsSameAssetSnapshot(
                        assetRecord,
                        library.Id,
                        fileInfo.Name,
                        assetType,
                        normalizedPath,
                        assetRecord.ContentHash,
                        currentHash,
                        fileInfo.Length,
                        fileInfo.LastWriteTimeUtc,
                        "ok"))
                {
                    Interlocked.Increment(ref stats.SkippedPersistCount);
                }
                else
                {
                    assetRecord = await CreateOrUpdateAssetAsync(
                        library,
                        assetUid: assetRecord.AssetUid,
                        assetName: fileInfo.Name,
                        assetType: assetType,
                        currentPath: normalizedPath,
                        contentHash: assetRecord.ContentHash,
                        observedHash: currentHash,
                        fileSize: fileInfo.Length,
                        modifiedTimeUtc: fileInfo.LastWriteTimeUtc,
                        status: "ok",
                        scanAt: scanAt,
                        createdAt: assetRecord.CreatedAt,
                        createdBy: assetRecord.CreatedBy,
                        uidVersion: assetRecord.UidVersion);
                    await EnsureAssetMetadataAsync(assetRecord.AssetUid, "ready", scanAt, ct);
                }
                stage = "已识别";
                aiState = "按内容指纹补写 uid";
            }
            if (assetRecord is null)
            {
                var assetUid = GenerateAssetUid();
                WriteUidSidecar(sidecarPath, assetUid);
                assetRecord = await CreateOrUpdateAssetAsync(
                    library,
                    assetUid: assetUid,
                    assetName: fileInfo.Name,
                    assetType: assetType,
                    currentPath: normalizedPath,
                    contentHash: currentHash,
                    observedHash: currentHash,
                    fileSize: fileInfo.Length,
                    modifiedTimeUtc: fileInfo.LastWriteTimeUtc,
                    status: "ok",
                    scanAt: scanAt);
                await EnsureAssetMetadataAsync(assetUid, "pending", scanAt, ct);
                stage = "新素材";
                aiState = "待生成描述与向量";
            }
        }

        var isDescribed = descriptionTableExists && HasDescription(connection, assetRecord.AssetUid, normalizedPath, assetRecord.ContentHash);
        var metadataTags = ReadMetadataTags(connection, assetRecord.AssetUid);
        var tags = BuildTags(assetType, extension, metadataTags);
        var summary = BuildSummary(assetType, fileInfo.Length, fileInfo.LastWriteTime, assetRecord.AssetUid, assetRecord.Status);

        return new ManagedAssetRecord
        {
            AssetUid = assetRecord.AssetUid,
            Name = fileInfo.Name,
            LibraryName = library.Name,
            AssetType = assetType,
            RelativePath = relativePath,
            LocalPath = normalizedPath,
            ContentHash = assetRecord.ContentHash,
            ObservedHash = assetRecord.ObservedHash,
            MetadataStatus = assetRecord.Status,
            FileSize = fileInfo.Length,
            ModifiedTimeUtc = fileInfo.LastWriteTimeUtc,
            HasUidSidecar = hasUidSidecar,
            IsDescribed = isDescribed,
            Summary = summary,
            Tags = new(tags),
            Stage = stage,
            AiState = isDescribed ? "已生成描述" : aiState
        };
    }

    private Task<AssetDbRecord> CreateOrUpdateAssetAsync(
        LibraryWorkspace library,
        string assetUid,
        string assetName,
        string assetType,
        string currentPath,
        string contentHash,
        string observedHash,
        long fileSize,
        DateTime modifiedTimeUtc,
        string status,
        DateTimeOffset scanAt,
        DateTimeOffset? createdAt = null,
        string? createdBy = null,
        int uidVersion = 1,
        CancellationToken ct = default)
    {
        return WriteQueue.EnqueueAsync(async token =>
        {
            var actualCreatedAt = createdAt ?? scanAt;
            var actualCreatedBy = string.IsNullOrWhiteSpace(createdBy) ? SystemCreatedBy : createdBy;

            await using var connection = await AssetDatabase.OpenConnectionAsync(token);

            await using var command = connection.CreateCommand();
            command.CommandText = """
            INSERT INTO assets (
                asset_uid,
                library_id,
                asset_name,
                asset_type,
                current_path,
                content_hash,
                observed_hash,
                file_size,
                modified_time_utc,
                status,
                created_at,
                updated_at,
                created_by,
                uid_version
            )
            VALUES (
                $asset_uid,
                $library_id,
                $asset_name,
                $asset_type,
                $current_path,
                $content_hash,
                $observed_hash,
                $file_size,
                $modified_time_utc,
                $status,
                $created_at,
                $updated_at,
                $created_by,
                $uid_version
            )
            ON CONFLICT(asset_uid) DO UPDATE SET
                library_id = excluded.library_id,
                asset_name = excluded.asset_name,
                asset_type = excluded.asset_type,
                current_path = excluded.current_path,
                content_hash = excluded.content_hash,
                observed_hash = excluded.observed_hash,
                file_size = excluded.file_size,
                modified_time_utc = excluded.modified_time_utc,
                status = excluded.status,
                updated_at = excluded.updated_at,
                uid_version = excluded.uid_version;
            """;

            AddParameter(command, "$asset_uid", assetUid);
            AddParameter(command, "$library_id", library.Id);
            AddParameter(command, "$asset_name", assetName);
            AddParameter(command, "$asset_type", assetType);
            AddParameter(command, "$current_path", currentPath);
            AddParameter(command, "$content_hash", contentHash);
            AddParameter(command, "$observed_hash", observedHash);
            AddParameter(command, "$file_size", fileSize);
            AddParameter(command, "$modified_time_utc", modifiedTimeUtc.ToString("O"));
            AddParameter(command, "$status", status);
            AddParameter(command, "$created_at", actualCreatedAt.ToString("O"));
            AddParameter(command, "$updated_at", scanAt.ToString("O"));
            AddParameter(command, "$created_by", actualCreatedBy);
            AddParameter(command, "$uid_version", uidVersion);
            await command.ExecuteNonQueryAsync(token);

            return new AssetDbRecord(
                assetUid,
                library.Id,
                assetName,
                assetType,
                currentPath,
                contentHash,
                observedHash,
                fileSize,
                modifiedTimeUtc,
                status,
                actualCreatedAt,
                scanAt,
                actualCreatedBy,
                uidVersion);
        }, ct).AsTask();
    }

    private Task EnsureAssetMetadataAsync(string assetUid, string metadataStatus, DateTimeOffset scanAt, CancellationToken ct)
    {
        return WriteQueue.EnqueueAsync(async token =>
        {
            await using var connection = await AssetDatabase.OpenConnectionAsync(token);

            await using var command = connection.CreateCommand();
            command.CommandText = """
            INSERT INTO asset_metadata (
                asset_uid,
                tags_json,
                metadata_status,
                vector_state,
                created_at,
                updated_at
            )
            VALUES (
                $asset_uid,
                '[]',
                $metadata_status,
                'pending',
                $created_at,
                $updated_at
            )
            ON CONFLICT(asset_uid) DO UPDATE SET
                metadata_status = excluded.metadata_status,
                updated_at = excluded.updated_at;
            """;

            AddParameter(command, "$asset_uid", assetUid);
            AddParameter(command, "$metadata_status", metadataStatus);
            AddParameter(command, "$created_at", scanAt.ToString("O"));
            AddParameter(command, "$updated_at", scanAt.ToString("O"));
            await command.ExecuteNonQueryAsync(token);
        }, ct).AsTask();
    }

    private static bool IsSameAssetSnapshot(
        AssetDbRecord assetRecord,
        string libraryId,
        string assetName,
        string assetType,
        string currentPath,
        string contentHash,
        string observedHash,
        long fileSize,
        DateTime modifiedTimeUtc,
        string status)
    {
        return string.Equals(assetRecord.LibraryId, libraryId, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(assetRecord.AssetName, assetName, StringComparison.Ordinal) &&
               string.Equals(assetRecord.AssetType, assetType, StringComparison.Ordinal) &&
               string.Equals(assetRecord.CurrentPath, currentPath, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(assetRecord.ContentHash, contentHash, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(assetRecord.ObservedHash, observedHash, StringComparison.OrdinalIgnoreCase) &&
               assetRecord.FileSize == fileSize &&
               assetRecord.ModifiedTimeUtc == modifiedTimeUtc &&
               string.Equals(assetRecord.Status, status, StringComparison.OrdinalIgnoreCase);
    }

    private string[] ReadMetadataTags(SqliteConnection connection, string assetUid)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT tags_json
            FROM asset_metadata
            WHERE asset_uid = $asset_uid
            LIMIT 1;
            """;
        AddParameter(command, "$asset_uid", assetUid);

        var tagsJson = command.ExecuteScalar() as string;
        if (string.IsNullOrWhiteSpace(tagsJson))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<string[]>(tagsJson, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private AssetDbRecord? TryGetAssetByUid(SqliteConnection connection, string assetUid)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                asset_uid,
                library_id,
                asset_name,
                asset_type,
                current_path,
                content_hash,
                observed_hash,
                file_size,
                modified_time_utc,
                status,
                created_at,
                updated_at,
                created_by,
                uid_version
            FROM assets
            WHERE asset_uid = $asset_uid
            LIMIT 1;
            """;
        AddParameter(command, "$asset_uid", assetUid);

        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadAssetDbRecord(reader) : null;
    }

    private AssetDbRecord? TryGetAssetByContentHash(SqliteConnection connection, string contentHash)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                asset_uid,
                library_id,
                asset_name,
                asset_type,
                current_path,
                content_hash,
                observed_hash,
                file_size,
                modified_time_utc,
                status,
                created_at,
                updated_at,
                created_by,
                uid_version
            FROM assets
            WHERE content_hash = $content_hash
               OR observed_hash = $content_hash
            ORDER BY updated_at DESC
            LIMIT 1;
            """;
        AddParameter(command, "$content_hash", contentHash);

        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadAssetDbRecord(reader) : null;
    }

    private static AssetDbRecord ReadAssetDbRecord(SqliteDataReader reader)
    {
        return new AssetDbRecord(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetInt64(7),
            DateTimeOffset.Parse(reader.GetString(8), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).UtcDateTime,
            reader.GetString(9),
            DateTimeOffset.Parse(reader.GetString(10)),
            DateTimeOffset.Parse(reader.GetString(11)),
            reader.GetString(12),
            reader.GetInt32(13));
    }

    private static bool HasDescription(SqliteConnection connection, string assetUid, string currentPath, string contentHash)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT 1
            FROM asset_descriptions
            WHERE asset_id = $asset_uid
               OR asset_path = $current_path
               OR (
                    $content_hash <> ''
                    AND content_hash IS NOT NULL
                    AND content_hash = $content_hash
               )
            LIMIT 1;
            """;
        AddParameter(command, "$asset_uid", assetUid);
        AddParameter(command, "$current_path", currentPath);
        AddParameter(command, "$content_hash", contentHash ?? string.Empty);

        return command.ExecuteScalar() is not null;
    }

    private bool DescriptionTableExists()
    {
        using var connection = AssetDatabase.OpenConnection();
        return TableExists(connection, "asset_descriptions");
    }

    private static bool TableExists(SqliteConnection connection, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT 1
            FROM sqlite_master
            WHERE type = 'table'
              AND name = $table_name
            LIMIT 1;
            """;
        AddParameter(command, "$table_name", tableName);
        return command.ExecuteScalar() is not null;
    }

    private static string[] BuildTags(string assetType, string extension, IEnumerable<string> metadataTags)
    {
        return metadataTags
            .Append(assetType)
            .Append(extension)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string BuildSummary(string assetType, long bytes, DateTime modifiedTime, string assetUid, string status)
    {
        var statusText = status switch
        {
            "changed" => "内容变化",
            "ok" => "已同步",
            _ => status
        };

        return $"{assetType}文件 · {FormatFileSize(bytes)} · 修改于 {modifiedTime:yyyy-MM-dd HH:mm} · {statusText} · UID {assetUid}";
    }

    private static string GenerateAssetUid()
    {
        return $"asset_{Guid.NewGuid():N}";
    }

    private string? TryReadUidSidecar(string sidecarPath, FileInfo sidecarInfo, out bool hasSidecar)
    {
        hasSidecar = sidecarInfo.Exists;
        if (!hasSidecar)
        {
            return null;
        }

        if (UidSidecarCache.TryGetValue(sidecarPath, out var cached) &&
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
            CacheUidSidecar(sidecarPath, sidecarInfo, uid);
            return uid;
        }
        catch
        {
            return null;
        }
    }

    private void WriteUidSidecar(string sidecarPath, string assetUid)
    {
        var document = new UidSidecarDocument
        {
            Uid = assetUid,
            Version = 1,
            CreatedBy = SystemCreatedBy
        };

        File.WriteAllText(sidecarPath, JsonSerializer.Serialize(document, JsonOptions));
        CacheUidSidecar(sidecarPath, new FileInfo(sidecarPath), assetUid);
    }

    private static string GetUidSidecarPath(string assetPath)
    {
        return assetPath + ".uid";
    }

    private string GetContentHash(string path, FileInfo fileInfo, ScanHashStats stats)
    {
        if (ContentHashCache.TryGetValue(path, out var cached) &&
            cached.Length == fileInfo.Length &&
            cached.LastWriteTimeUtc == fileInfo.LastWriteTimeUtc)
        {
            Interlocked.Increment(ref stats.ReusedHashCount);
            return cached.Hash;
        }

        Interlocked.Increment(ref stats.RecomputedHashCount);
        var hash = ComputeContentHash(path);
        CacheContentHash(path, fileInfo, hash);
        return hash;
    }

    private static string ComputeContentHash(string path)
    {
        using var stream = File.OpenRead(path);
        var bytes = SHA256.HashData(stream);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private bool CanReuseStoredHash(AssetDbRecord assetRecord, FileInfo fileInfo)
    {
        return assetRecord.FileSize == fileInfo.Length &&
               assetRecord.ModifiedTimeUtc == fileInfo.LastWriteTimeUtc;
    }

    private void CacheUidSidecar(string sidecarPath, FileInfo sidecarInfo, string? uid)
    {
        UidSidecarCache[sidecarPath] = new CachedUidSidecar(
            sidecarInfo.Length,
            sidecarInfo.LastWriteTimeUtc,
            uid);
    }

    private void CacheContentHash(string path, FileInfo fileInfo, string hash)
    {
        ContentHashCache[path] = new CachedContentHash(
            fileInfo.Length,
            fileInfo.LastWriteTimeUtc,
            hash);
    }

    private IEnumerable<string> EnumerateSupportedFiles(string rootPath)
    {
        if (!Directory.Exists(rootPath))
        {
            yield break;
        }

        var pending = new Stack<string>();
        pending.Push(rootPath);

        while (pending.Count > 0)
        {
            var current = pending.Pop();

            IEnumerable<string> subDirectories;
            try
            {
                subDirectories = Directory.EnumerateDirectories(current);
            }
            catch
            {
                continue;
            }

            foreach (var subDirectory in subDirectories)
            {
                pending.Push(subDirectory);
            }

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(current);
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                if (TryClassifyAssetType(file, out _))
                {
                    yield return file;
                }
            }
        }
    }

    private static string BuildLibraryName(string normalizedPath, IEnumerable<LibraryStoreItem> existing)
    {
        var baseName = Path.GetFileName(normalizedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = normalizedPath;
        }

        var used = new HashSet<string>(existing.Select(item => item.Name), StringComparer.OrdinalIgnoreCase);
        if (!used.Contains(baseName))
        {
            return baseName;
        }

        var index = 2;
        while (used.Contains($"{baseName} {index}"))
        {
            index++;
        }

        return $"{baseName} {index}";
    }

    private static string ClassifyAssetType(string path)
    {
        return TryClassifyAssetType(path, out var type) ? type : "其他";
    }

    private static bool TryClassifyAssetType(string path, out string type)
    {
        var extension = Path.GetExtension(path);
        if (TextExtensions.Contains(extension))
        {
            type = "文本";
            return true;
        }

        if (ImageExtensions.Contains(extension))
        {
            type = "图片";
            return true;
        }

        if (VideoExtensions.Contains(extension))
        {
            type = "视频";
            return true;
        }

        if (AudioExtensions.Contains(extension))
        {
            type = "音频";
            return true;
        }

        type = string.Empty;
        return false;
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        if (bytes < 1024 * 1024)
        {
            return $"{bytes / 1024d:F1} KB";
        }

        if (bytes < 1024L * 1024 * 1024)
        {
            return $"{bytes / 1024d / 1024:F1} MB";
        }

        return $"{bytes / 1024d / 1024 / 1024:F1} GB";
    }

    private async Task<List<LibraryStoreItem>> ReadStoreAsync(CancellationToken ct)
    {
        if (!File.Exists(LibraryStorePath))
        {
            return [];
        }

        await using var stream = File.OpenRead(LibraryStorePath);
        return await JsonSerializer.DeserializeAsync<List<LibraryStoreItem>>(stream, JsonOptions, ct) ?? [];
    }

    private async Task WriteStoreAsync(List<LibraryStoreItem> items, CancellationToken ct)
    {
        await using var stream = File.Create(LibraryStorePath);
        await JsonSerializer.SerializeAsync(stream, items, JsonOptions, ct);
    }

    private static void AddParameter(SqliteCommand command, string name, object? value)
    {
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    }

    private sealed class LibraryStoreItem
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string RootPath { get; set; } = string.Empty;
    }

    private sealed class UidSidecarDocument
    {
        public string Uid { get; set; } = string.Empty;
        public int Version { get; set; }
        public string CreatedBy { get; set; } = string.Empty;
    }

    private sealed class ScanHashStats
    {
        public int RecomputedHashCount;
        public int ReusedHashCount;
        public int SkippedPersistCount;
    }

    private sealed record CachedUidSidecar(long Length, DateTime LastWriteTimeUtc, string? Uid);

    private sealed record CachedContentHash(long Length, DateTime LastWriteTimeUtc, string Hash);

    private sealed record AssetDbRecord(
        string AssetUid,
        string LibraryId,
        string AssetName,
        string AssetType,
        string CurrentPath,
        string ContentHash,
        string ObservedHash,
        long FileSize,
        DateTime ModifiedTimeUtc,
        string Status,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        string CreatedBy,
        int UidVersion);
}
