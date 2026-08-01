using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using AssetsLibrarySystem.Application.Models;
using AssetsLibrarySystem.Application.Services.Infrastructure;
using Microsoft.Data.Sqlite;

namespace AssetsLibrarySystem.Application.Services.AssetDescription;

public sealed class AssetDescriptionStore : IAssetDescriptionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private IDatabaseWriteQueue WriteQueue { get; }
    private IAssetDatabase AssetDatabase { get; }

    public AssetDescriptionStore(IDatabaseWriteQueue writeQueue, IAssetDatabase assetDatabase)
    {
        WriteQueue = writeQueue;
        AssetDatabase = assetDatabase;
    }

    public string DatabasePath => AssetDatabase.DatabasePath;

    public async Task SaveAsync(AssetDescriptionDocument document, CancellationToken ct = default)
    {
        await AssetDatabase.EnsureSchemaAsync(ct);
        await WriteQueue.EnqueueAsync(async token =>
        {
            await using var connection = await AssetDatabase.OpenConnectionAsync(token);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(token).ConfigureAwait(false);

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
            INSERT INTO asset_descriptions (
                asset_id,
                asset_name,
                asset_type,
                asset_path,
                description,
                backend_endpoint,
                mode,
                generated_at,
                token_usage_json,
                prompt,
                system_prompt,
                content_hash,
                metadata_status
            )
            VALUES (
                $asset_id,
                $asset_name,
                $asset_type,
                $asset_path,
                $description,
                $backend_endpoint,
                $mode,
                $generated_at,
                $token_usage_json,
                $prompt,
                $system_prompt,
                $content_hash,
                $metadata_status
            )
            ON CONFLICT(asset_id) DO UPDATE SET
                asset_name = excluded.asset_name,
                asset_type = excluded.asset_type,
                asset_path = excluded.asset_path,
                description = excluded.description,
                backend_endpoint = excluded.backend_endpoint,
                mode = excluded.mode,
                generated_at = excluded.generated_at,
                token_usage_json = excluded.token_usage_json,
                prompt = excluded.prompt,
                system_prompt = excluded.system_prompt,
                content_hash = excluded.content_hash,
                metadata_status = excluded.metadata_status;
            """;

            AddParameter(command, "$asset_id", document.AssetId);
            AddParameter(command, "$asset_name", document.AssetName);
            AddParameter(command, "$asset_type", document.AssetType);
            AddParameter(command, "$asset_path", document.CurrentPath);
            AddParameter(command, "$description", document.Description);
            AddParameter(command, "$backend_endpoint", document.BackendEndpoint);
            AddParameter(command, "$mode", document.Mode);
            AddParameter(command, "$generated_at", document.GeneratedAt.ToString("O"));
            AddParameter(command, "$token_usage_json", SerializeTokenUsage(document.TokenUsage));
            AddParameter(command, "$prompt", (object?)document.Prompt ?? DBNull.Value);
            AddParameter(command, "$system_prompt", (object?)document.SystemPrompt ?? DBNull.Value);
            AddParameter(command, "$content_hash", (object?)document.ContentHash ?? DBNull.Value);
            // 新生成的描述恢复为 ready（覆盖 stale）
            var metadataStatus = string.IsNullOrWhiteSpace(document.MetadataStatus) ||
                                 string.Equals(document.MetadataStatus, "changed", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(document.MetadataStatus, "stale", StringComparison.OrdinalIgnoreCase)
                ? "ready"
                : document.MetadataStatus;
            AddParameter(command, "$metadata_status", metadataStatus);

            await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            await UpdateAssetMetadataAsync(connection, transaction, document, token).ConfigureAwait(false);
            await transaction.CommitAsync(token).ConfigureAwait(false);
        }, ct);
    }

    public async Task<AssetDescriptionDocument?> TryGetAsync(long assetId, CancellationToken ct = default)
    {
        await using var connection = await AssetDatabase.OpenConnectionAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                d.asset_id,
                a.asset_uid,
                d.asset_name,
                d.asset_type,
                d.asset_path,
                d.description,
                d.backend_endpoint,
                d.mode,
                d.generated_at,
                d.token_usage_json,
                d.prompt,
                d.system_prompt,
                d.content_hash,
                d.metadata_status
            FROM asset_descriptions AS d
            INNER JOIN assets AS a ON a.id = d.asset_id
            WHERE d.asset_id = $asset_id
            LIMIT 1;
            """;
        AddParameter(command, "$asset_id", assetId);

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        return ReadDocument(reader);
    }

    public async Task<AssetDescriptionDocument?> TryGetForAssetAsync(ManagedAssetRecord asset, CancellationToken ct = default)
    {
        await using var connection = await AssetDatabase.OpenConnectionAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                d.asset_id,
                a.asset_uid,
                d.asset_name,
                d.asset_type,
                d.asset_path,
                d.description,
                d.backend_endpoint,
                d.mode,
                d.generated_at,
                d.token_usage_json,
                d.prompt,
                d.system_prompt,
                d.content_hash,
                d.metadata_status
            FROM asset_descriptions AS d
            INNER JOIN assets AS a ON a.id = d.asset_id
            WHERE d.asset_id = $asset_id
            ORDER BY d.generated_at DESC
            LIMIT 1;
            """;
        AddParameter(command, "$asset_id", asset.DatabaseId);

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        return ReadDocument(reader);
    }

    public async Task<bool> DeleteAsync(long assetId, CancellationToken ct = default)
    {
        await AssetDatabase.EnsureSchemaAsync(ct);
        return await WriteQueue.EnqueueAsync(async token =>
        {
            await using var connection = await AssetDatabase.OpenConnectionAsync(token);

            await using var command = connection.CreateCommand();
            command.CommandText = """
                DELETE FROM asset_descriptions
                WHERE asset_id = $asset_id;
                """;
            AddParameter(command, "$asset_id", assetId);
            var affectedRows = await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);

            if (affectedRows > 0)
            {
                await ResetAssetMetadataAsync(connection, assetId, token).ConfigureAwait(false);
            }

            return affectedRows > 0;
        }, ct);
    }

    public async Task UpdateDescriptionAsync(long assetId, string newDescription, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newDescription);
        await AssetDatabase.EnsureSchemaAsync(ct);
        await WriteQueue.EnqueueAsync(async token =>
        {
            await using var connection = await AssetDatabase.OpenConnectionAsync(token);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(token).ConfigureAwait(false);

            // 剪辑素材保护：编辑文本合并进 JSON 的「整体」字段，保留 segments 片段结构
            var mergedDescription = await MergeEditedDescriptionAsync(connection, assetId, newDescription, token).ConfigureAwait(false);

            // 更新描述文本，同时更新 generated_at 使向量化检测认为描述已更新
            await using var cmd1 = connection.CreateCommand();
            cmd1.Transaction = transaction;
            cmd1.CommandText = """
                UPDATE asset_descriptions
                SET description = $description,
                    generated_at = $generated_at,
                    metadata_status = 'edited'
                WHERE asset_id = $asset_id;
                """;
            AddParameter(cmd1, "$asset_id", assetId);
            AddParameter(cmd1, "$description", mergedDescription);
            AddParameter(cmd1, "$generated_at", DateTimeOffset.UtcNow.ToString("O"));
            var affected = await cmd1.ExecuteNonQueryAsync(token).ConfigureAwait(false);

            if (affected == 0)
            {
                await transaction.RollbackAsync(token).ConfigureAwait(false);
                throw new InvalidOperationException($"素材描述 (asset_id={assetId}) 不存在。");
            }

            // 标记向量为 pending，触发后续重新向量化
            await using var cmd2 = connection.CreateCommand();
            cmd2.Transaction = transaction;
            cmd2.CommandText = """
                UPDATE asset_metadata
                SET vector_state = 'pending',
                    updated_at = $updated_at
                WHERE asset_id = $asset_id;
                """;
            AddParameter(cmd2, "$asset_id", assetId);
            AddParameter(cmd2, "$updated_at", DateTimeOffset.UtcNow.ToString("O"));
            await cmd2.ExecuteNonQueryAsync(token).ConfigureAwait(false);

            await transaction.CommitAsync(token).ConfigureAwait(false);
        }, ct);
    }

    private static async Task<string> MergeEditedDescriptionAsync(
        SqliteConnection connection,
        long assetId,
        string newDescription,
        CancellationToken ct)
    {
        // 读取原描述，判断是否为剪辑 JSON（含 segments）
        await using var read = connection.CreateCommand();
        read.CommandText = "SELECT description FROM asset_descriptions WHERE asset_id = $asset_id;";
        AddParameter(read, "$asset_id", assetId);
        var existing = await read.ExecuteScalarAsync(ct).ConfigureAwait(false) as string;
        return StructuredDescriptionHelper.SetPrimaryText(existing, newDescription);
    }

    private static AssetDescriptionDocument ReadDocument(SqliteDataReader reader)
    {
        return new AssetDescriptionDocument(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetString(7),
            DateTimeOffset.Parse(reader.GetString(8)),
            DeserializeTokenUsage(reader.IsDBNull(9) ? null : reader.GetString(9)),
            reader.IsDBNull(10) ? null : reader.GetString(10),
            reader.IsDBNull(11) ? null : reader.GetString(11),
            reader.IsDBNull(12) ? null : reader.GetString(12),
            reader.IsDBNull(13) ? "ready" : reader.GetString(13));
    }

    private static void AddParameter(SqliteCommand command, string name, object? value)
    {
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    }

    private static string? SerializeTokenUsage(AssetDescriptionTokenUsage? tokenUsage)
    {
        return tokenUsage is null
            ? null
            : JsonSerializer.Serialize(tokenUsage, JsonOptions);
    }

    private static AssetDescriptionTokenUsage? DeserializeTokenUsage(string? json)
    {
        return string.IsNullOrWhiteSpace(json)
            ? null
            : JsonSerializer.Deserialize<AssetDescriptionTokenUsage>(json, JsonOptions);
    }

    private static async Task UpdateAssetMetadataAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        AssetDescriptionDocument document,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO asset_metadata (
                asset_id,
                tags_json,
                metadata_status,
                vector_state,
                subtype,
                created_at,
                updated_at
            )
            VALUES (
                $asset_id,
                '[]',
                'described',
                'pending',
                $subtype,
                $created_at,
                $updated_at
            )
            ON CONFLICT(asset_id) DO UPDATE SET
                metadata_status = excluded.metadata_status,
                vector_state = 'pending',
                subtype = COALESCE(excluded.subtype, asset_metadata.subtype),
                updated_at = excluded.updated_at;
            """;

        AddParameter(command, "$asset_id", document.AssetId);
        AddParameter(command, "$subtype", string.IsNullOrWhiteSpace(document.Subtype) ? DBNull.Value : document.Subtype.Trim());
        AddParameter(command, "$created_at", document.GeneratedAt.ToString("O"));
        AddParameter(command, "$updated_at", document.GeneratedAt.ToString("O"));
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task ResetAssetMetadataAsync(
        SqliteConnection connection,
        long assetId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE asset_metadata
            SET metadata_status = 'pending',
                updated_at = $updated_at
            WHERE asset_id = $asset_id;
            """;

        AddParameter(command, "$asset_id", assetId);
        AddParameter(command, "$updated_at", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}
