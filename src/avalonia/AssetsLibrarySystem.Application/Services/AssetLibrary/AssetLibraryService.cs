using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AssetsLibrarySystem.Avalonia.Infrastructure;
using AssetsLibrarySystem.Avalonia.Models;
using Microsoft.Data.Sqlite;

namespace AssetsLibrarySystem.Avalonia.Services.AssetLibrary;

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
    private string MetadataDatabasePath { get; }

    public AssetLibraryService()
    {
        LibraryStorePath = SharedDataPathHelper.GetDataFilePath("libraries.json");
        MetadataDatabasePath = SharedDataPathHelper.GetDataFilePath("asset_descriptions.db");
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
        return Task.Run<IReadOnlyList<ManagedAssetRecord>>(() => BuildRecordsForDirectory(library.RootPath, library), ct);
    }

    private List<ManagedAssetRecord> BuildRecordsForDirectory(string rootPath, LibraryWorkspace library)
    {
        EnsureMetadataSchema();

        using var connection = CreateMetadataConnection();
        connection.Open();

        var scanAt = DateTimeOffset.UtcNow;
        return EnumerateSupportedFiles(rootPath)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => ImportOrRefreshAsset(connection, library, path, scanAt))
            .ToList();
    }

    private ManagedAssetRecord ImportOrRefreshAsset(
        SqliteConnection connection,
        LibraryWorkspace library,
        string fullPath,
        DateTimeOffset scanAt)
    {
        var normalizedPath = Path.GetFullPath(fullPath);
        var relativePath = Path.GetRelativePath(library.RootPath, normalizedPath);
        var fileInfo = new FileInfo(normalizedPath);
        var assetType = ClassifyAssetType(normalizedPath);
        var extension = fileInfo.Extension.TrimStart('.').ToLowerInvariant();
        var currentHash = ComputeContentHash(normalizedPath);
        var sidecarPath = GetUidSidecarPath(normalizedPath);
        var sidecarUid = TryReadUidSidecar(sidecarPath);

        AssetDbRecord? assetRecord;
        string stage = "已识别";
        string aiState = "身份已确认";

        if (!string.IsNullOrWhiteSpace(sidecarUid))
        {
            assetRecord = TryGetAssetByUid(connection, sidecarUid!);
            if (assetRecord is null)
            {
                assetRecord = CreateOrUpdateAsset(
                    connection,
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
                EnsureAssetMetadata(connection, sidecarUid!, "pending", scanAt);
                stage = "已迁入";
                aiState = "待生成描述与向量";
            }
            else if (string.Equals(assetRecord.ContentHash, currentHash, StringComparison.OrdinalIgnoreCase))
            {
                assetRecord = CreateOrUpdateAsset(
                    connection,
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
                EnsureAssetMetadata(connection, assetRecord.AssetUid, "ready", scanAt);
                stage = "已同步";
                aiState = "身份已确认";
            }
            else
            {
                assetRecord = CreateOrUpdateAsset(
                    connection,
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
                EnsureAssetMetadata(connection, assetRecord.AssetUid, "changed", scanAt);
                stage = "内容已变化";
                aiState = "等待版本处理策略";
            }
        }
        else
        {
            assetRecord = TryGetAssetByContentHash(connection, currentHash);
            if (assetRecord is not null)
            {
                WriteUidSidecar(sidecarPath, assetRecord.AssetUid);
                assetRecord = CreateOrUpdateAsset(
                    connection,
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
                EnsureAssetMetadata(connection, assetRecord.AssetUid, "ready", scanAt);
                stage = "已识别";
                aiState = "按内容指纹补写 uid";
            }
            if (assetRecord is null)
            {
                var assetUid = GenerateAssetUid();
                WriteUidSidecar(sidecarPath, assetUid);
                assetRecord = CreateOrUpdateAsset(
                    connection,
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
                EnsureAssetMetadata(connection, assetUid, "pending", scanAt);
                stage = "新素材";
                aiState = "待生成描述与向量";
            }
        }

        UpsertAssetLocation(connection, assetRecord.AssetUid, normalizedPath, scanAt);

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
            HasUidSidecar = File.Exists(sidecarPath),
            Summary = summary,
            Tags = new(tags),
            Stage = stage,
            AiState = aiState
        };
    }

    private AssetDbRecord CreateOrUpdateAsset(
        SqliteConnection connection,
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
        int uidVersion = 1)
    {
        var actualCreatedAt = createdAt ?? scanAt;
        var actualCreatedBy = string.IsNullOrWhiteSpace(createdBy) ? SystemCreatedBy : createdBy;

        using var command = connection.CreateCommand();
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
        command.ExecuteNonQuery();

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
    }

    private void EnsureAssetMetadata(SqliteConnection connection, string assetUid, string metadataStatus, DateTimeOffset scanAt)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO asset_metadata (
                asset_uid,
                description_text,
                tags_json,
                metadata_status,
                vector_state,
                created_at,
                updated_at
            )
            VALUES (
                $asset_uid,
                NULL,
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
        command.ExecuteNonQuery();
    }

    private void UpsertAssetLocation(SqliteConnection connection, string assetUid, string currentPath, DateTimeOffset scanAt)
    {
        using var resetCommand = connection.CreateCommand();
        resetCommand.CommandText = """
            UPDATE asset_locations
            SET is_current = 0
            WHERE asset_uid = $asset_uid
              AND path <> $path;
            """;
        AddParameter(resetCommand, "$asset_uid", assetUid);
        AddParameter(resetCommand, "$path", currentPath);
        resetCommand.ExecuteNonQuery();

        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO asset_locations (
                asset_uid,
                path,
                first_seen_at,
                last_seen_at,
                is_current
            )
            VALUES (
                $asset_uid,
                $path,
                $first_seen_at,
                $last_seen_at,
                1
            )
            ON CONFLICT(asset_uid, path) DO UPDATE SET
                last_seen_at = excluded.last_seen_at,
                is_current = 1;
            """;

        AddParameter(command, "$asset_uid", assetUid);
        AddParameter(command, "$path", currentPath);
        AddParameter(command, "$first_seen_at", scanAt.ToString("O"));
        AddParameter(command, "$last_seen_at", scanAt.ToString("O"));
        command.ExecuteNonQuery();
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
            DateTime.Parse(reader.GetString(8)),
            reader.GetString(9),
            DateTimeOffset.Parse(reader.GetString(10)),
            DateTimeOffset.Parse(reader.GetString(11)),
            reader.GetString(12),
            reader.GetInt32(13));
    }

    private void EnsureMetadataSchema()
    {
        using var connection = CreateMetadataConnection();
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS assets (
                asset_uid TEXT PRIMARY KEY,
                library_id TEXT NOT NULL,
                asset_name TEXT NOT NULL,
                asset_type TEXT NOT NULL,
                current_path TEXT NOT NULL,
                content_hash TEXT NOT NULL,
                observed_hash TEXT NOT NULL,
                file_size INTEGER NOT NULL,
                modified_time_utc TEXT NOT NULL,
                status TEXT NOT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                created_by TEXT NOT NULL,
                uid_version INTEGER NOT NULL DEFAULT 1
            );

            CREATE INDEX IF NOT EXISTS ix_assets_content_hash
                ON assets(content_hash);

            CREATE INDEX IF NOT EXISTS ix_assets_current_path
                ON assets(current_path);

            CREATE TABLE IF NOT EXISTS asset_metadata (
                asset_uid TEXT PRIMARY KEY,
                description_text TEXT NULL,
                tags_json TEXT NOT NULL DEFAULT '[]',
                metadata_status TEXT NOT NULL,
                vector_state TEXT NOT NULL DEFAULT 'pending',
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS asset_locations (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                asset_uid TEXT NOT NULL,
                path TEXT NOT NULL,
                first_seen_at TEXT NOT NULL,
                last_seen_at TEXT NOT NULL,
                is_current INTEGER NOT NULL DEFAULT 0,
                UNIQUE(asset_uid, path)
            );

            CREATE INDEX IF NOT EXISTS ix_asset_locations_uid_current
                ON asset_locations(asset_uid, is_current);
            """;
        command.ExecuteNonQuery();
    }

    private SqliteConnection CreateMetadataConnection()
    {
        return new SqliteConnection($"Data Source={MetadataDatabasePath}");
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

    private string? TryReadUidSidecar(string sidecarPath)
    {
        if (!File.Exists(sidecarPath))
        {
            return null;
        }

        try
        {
            var content = File.ReadAllText(sidecarPath);
            var document = JsonSerializer.Deserialize<UidSidecarDocument>(content, JsonOptions);
            return string.IsNullOrWhiteSpace(document?.Uid) ? null : document.Uid.Trim();
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
    }

    private static string GetUidSidecarPath(string assetPath)
    {
        return assetPath + ".uid";
    }

    private static string ComputeContentHash(string path)
    {
        using var stream = File.OpenRead(path);
        var bytes = SHA256.HashData(stream);
        return Convert.ToHexString(bytes).ToLowerInvariant();
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
