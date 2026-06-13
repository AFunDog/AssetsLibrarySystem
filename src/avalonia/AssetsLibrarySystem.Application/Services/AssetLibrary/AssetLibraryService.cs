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

    private IDatabaseWriteQueue WriteQueue { get; }
    private IAssetDatabase AssetDatabase { get; }
    private ConcurrentDictionary<string, CachedUidSidecar> UidSidecarCache { get; } = new(StringComparer.OrdinalIgnoreCase);
    private ConcurrentDictionary<string, CachedContentHash> ContentHashCache { get; } = new(StringComparer.OrdinalIgnoreCase);

    public AssetLibraryService(IDatabaseWriteQueue writeQueue, IAssetDatabase assetDatabase)
    {
        WriteQueue = writeQueue;
        AssetDatabase = assetDatabase;
    }

    public async Task<IReadOnlyList<LibraryWorkspace>> GetLibrariesAsync(CancellationToken ct = default)
    {
        await using var connection = await AssetDatabase.OpenConnectionAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT l.id, l.name, l.root_path, COUNT(a.id)
            FROM libraries AS l
            LEFT JOIN assets AS a ON a.library_id = l.id
            GROUP BY l.id, l.name, l.root_path
            ORDER BY l.name;
            """;
        await using var reader = await command.ExecuteReaderAsync(ct);
        var items = new List<LibraryWorkspace>();
        while (await reader.ReadAsync(ct))
        {
            items.Add(new LibraryWorkspace(reader.GetInt64(0), reader.GetString(1), reader.GetString(2),
                "素材库信息已存储在数据库中。", "已登记目录", reader.GetInt32(3)));
        }
        return items;
    }

    public async Task<LibraryWorkspace> AddLibraryAsync(string folderPath, CancellationToken ct = default)
    {
        var normalizedPath = Path.GetFullPath(folderPath);
        if (!Directory.Exists(normalizedPath))
        {
            throw new DirectoryNotFoundException($"目录不存在：{normalizedPath}");
        }

        var items = await GetLibrariesAsync(ct);
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

        var name = BuildLibraryName(normalizedPath, items);
        var now = DateTimeOffset.UtcNow.ToString("O");
        var id = await WriteQueue.EnqueueAsync(async token =>
        {
            await using var connection = await AssetDatabase.OpenConnectionAsync(token);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO libraries (name, root_path, created_at, updated_at)
                VALUES ($name, $root_path, $created_at, $updated_at);
                SELECT last_insert_rowid();
                """;
            AddParameter(command, "$name", name);
            AddParameter(command, "$root_path", normalizedPath);
            AddParameter(command, "$created_at", now);
            AddParameter(command, "$updated_at", now);
            return Convert.ToInt64(await command.ExecuteScalarAsync(token), CultureInfo.InvariantCulture);
        }, ct);
        return new LibraryWorkspace(id, name, normalizedPath, "目录已登记，等待首次扫描。", "已登记目录", 0);
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
                await EnsureAssetMetadataAsync(assetRecord.Id, "pending", scanAt, ct);
                stage = "已迁入";
                aiState = "待生成描述与向量";
            }
            else if (CanReuseStoredHash(assetRecord, fileInfo))
            {
                currentHash = assetRecord.ContentHash;
                stats.IncrementReusedHashCount();
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
                    stats.IncrementSkippedPersistCount();
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
                    await EnsureAssetMetadataAsync(assetRecord.Id, "ready", scanAt, ct);
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
                        stats.IncrementSkippedPersistCount();
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
                        await EnsureAssetMetadataAsync(assetRecord.Id, "ready", scanAt, ct);
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
                    await EnsureAssetMetadataAsync(assetRecord.Id, "changed", scanAt, ct);
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
                    stats.IncrementSkippedPersistCount();
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
                    await EnsureAssetMetadataAsync(assetRecord.Id, "ready", scanAt, ct);
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
                await EnsureAssetMetadataAsync(assetRecord.Id, "pending", scanAt, ct);
                stage = "新素材";
                aiState = "待生成描述与向量";
            }
        }

        var isDescribed = descriptionTableExists && HasDescription(connection, assetRecord.Id);
        var isVectorized = HasVector(connection, assetRecord.Id);
        var metadataTags = ReadMetadataTags(connection, assetRecord.Id);
        var tags = BuildTags(assetType, extension, metadataTags);
        var summary = BuildSummary(assetType, fileInfo.Length, fileInfo.LastWriteTime, assetRecord.AssetUid, assetRecord.Status);

        return new ManagedAssetRecord
        {
            DatabaseId = assetRecord.Id,
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
            IsVectorized = isVectorized,
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

            await using var idCommand = connection.CreateCommand();
            idCommand.CommandText = "SELECT id FROM assets WHERE asset_uid = $asset_uid LIMIT 1;";
            AddParameter(idCommand, "$asset_uid", assetUid);
            var id = Convert.ToInt64(await idCommand.ExecuteScalarAsync(token), CultureInfo.InvariantCulture);

            return new AssetDbRecord(
                id,
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

    private Task EnsureAssetMetadataAsync(long assetId, string metadataStatus, DateTimeOffset scanAt, CancellationToken ct)
    {
        return WriteQueue.EnqueueAsync(async token =>
        {
            await using var connection = await AssetDatabase.OpenConnectionAsync(token);

            await using var command = connection.CreateCommand();
            command.CommandText = """
            INSERT INTO asset_metadata (
                asset_id,
                tags_json,
                metadata_status,
                vector_state,
                created_at,
                updated_at
            )
            VALUES (
                $asset_id,
                '[]',
                $metadata_status,
                'pending',
                $created_at,
                $updated_at
            )
            ON CONFLICT(asset_id) DO UPDATE SET
                metadata_status = excluded.metadata_status,
                updated_at = excluded.updated_at;
            """;

            AddParameter(command, "$asset_id", assetId);
            AddParameter(command, "$metadata_status", metadataStatus);
            AddParameter(command, "$created_at", scanAt.ToString("O"));
            AddParameter(command, "$updated_at", scanAt.ToString("O"));
            await command.ExecuteNonQueryAsync(token);
        }, ct).AsTask();
    }

    private static bool IsSameAssetSnapshot(
        AssetDbRecord assetRecord,
        long libraryId,
        string assetName,
        string assetType,
        string currentPath,
        string contentHash,
        string observedHash,
        long fileSize,
        DateTime modifiedTimeUtc,
        string status)
    {
        return assetRecord.LibraryId == libraryId &&
               string.Equals(assetRecord.AssetName, assetName, StringComparison.Ordinal) &&
               string.Equals(assetRecord.AssetType, assetType, StringComparison.Ordinal) &&
               string.Equals(assetRecord.CurrentPath, currentPath, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(assetRecord.ContentHash, contentHash, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(assetRecord.ObservedHash, observedHash, StringComparison.OrdinalIgnoreCase) &&
               assetRecord.FileSize == fileSize &&
               assetRecord.ModifiedTimeUtc == modifiedTimeUtc &&
               string.Equals(assetRecord.Status, status, StringComparison.OrdinalIgnoreCase);
    }

    private string[] ReadMetadataTags(SqliteConnection connection, long assetId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT tags_json
            FROM asset_metadata
            WHERE asset_id = $asset_id
            LIMIT 1;
            """;
        AddParameter(command, "$asset_id", assetId);

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
                id,
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
                id,
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
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetInt64(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetString(7),
            reader.GetInt64(8),
            DateTimeOffset.Parse(reader.GetString(9), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).UtcDateTime,
            reader.GetString(10),
            DateTimeOffset.Parse(reader.GetString(11)),
            DateTimeOffset.Parse(reader.GetString(12)),
            reader.GetString(13),
            reader.GetInt32(14));
    }

    private static bool HasDescription(SqliteConnection connection, long assetId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT 1
            FROM asset_descriptions
            WHERE asset_id = $asset_id
            LIMIT 1;
            """;
        AddParameter(command, "$asset_id", assetId);

        return command.ExecuteScalar() is not null;
    }

    private static bool HasVector(SqliteConnection connection, long assetId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT 1
            FROM asset_description_vectors
            WHERE asset_id = $asset_id
            LIMIT 1;
            """;
        AddParameter(command, "$asset_id", assetId);
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
            stats.IncrementReusedHashCount();
            return cached.Hash;
        }

        stats.IncrementRecomputedHashCount();
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

    private static string BuildLibraryName(string normalizedPath, IEnumerable<LibraryWorkspace> existing)
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

    private static void AddParameter(SqliteCommand command, string name, object? value)
    {
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    }

    private sealed class UidSidecarDocument
    {
        public string Uid { get; set; } = string.Empty;
        public int Version { get; set; }
        public string CreatedBy { get; set; } = string.Empty;
    }

    private sealed class ScanHashStats
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

    private sealed record CachedUidSidecar(long Length, DateTime LastWriteTimeUtc, string? Uid);

    private sealed record CachedContentHash(long Length, DateTime LastWriteTimeUtc, string Hash);

    private sealed record AssetDbRecord(
        long Id,
        string AssetUid,
        long LibraryId,
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
