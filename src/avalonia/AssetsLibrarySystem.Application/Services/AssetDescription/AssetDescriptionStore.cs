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

            await using var command = connection.CreateCommand();
            command.CommandText = """
            INSERT INTO asset_descriptions (
                asset_id,
                asset_name,
                asset_type,
                asset_path,
                store_path,
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
                $store_path,
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
                store_path = excluded.store_path,
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

            AddParameter(command, "$asset_id", document.AssetUid);
            AddParameter(command, "$asset_name", document.AssetName);
            AddParameter(command, "$asset_type", document.AssetType);
            AddParameter(command, "$asset_path", document.CurrentPath);
            AddParameter(command, "$store_path", document.StorePath);
            AddParameter(command, "$description", document.Description);
            AddParameter(command, "$backend_endpoint", document.BackendEndpoint);
            AddParameter(command, "$mode", document.Mode);
            AddParameter(command, "$generated_at", document.GeneratedAt.ToString("O"));
            AddParameter(command, "$token_usage_json", SerializeTokenUsage(document.TokenUsage));
            AddParameter(command, "$prompt", (object?)document.Prompt ?? DBNull.Value);
            AddParameter(command, "$system_prompt", (object?)document.SystemPrompt ?? DBNull.Value);
            AddParameter(command, "$content_hash", (object?)document.ContentHash ?? DBNull.Value);
            AddParameter(command, "$metadata_status", document.MetadataStatus);

            await command.ExecuteNonQueryAsync(token);

            await UpdateAssetMetadataAsync(connection, document, token);
        }, ct);
    }

    public async Task<AssetDescriptionDocument?> TryGetAsync(string assetId, CancellationToken ct = default)
    {
        await using var connection = await AssetDatabase.OpenConnectionAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                asset_id,
                asset_name,
                asset_type,
                asset_path,
                store_path,
                description,
                backend_endpoint,
                mode,
                generated_at,
                token_usage_json,
                prompt,
                system_prompt,
                content_hash,
                metadata_status
            FROM asset_descriptions
            WHERE asset_id = $asset_id
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
                asset_id,
                asset_name,
                asset_type,
                asset_path,
                store_path,
                description,
                backend_endpoint,
                mode,
                generated_at,
                token_usage_json,
                prompt,
                system_prompt,
                content_hash,
                metadata_status
            FROM asset_descriptions
            WHERE asset_id = $asset_uid
               OR asset_path = $current_path
               OR (
                    $content_hash <> ''
                    AND content_hash IS NOT NULL
                    AND content_hash = $content_hash
               )
            ORDER BY
                CASE
                    WHEN asset_id = $asset_uid THEN 0
                    WHEN asset_path = $current_path THEN 1
                    ELSE 2
                END,
                generated_at DESC
            LIMIT 1;
            """;
        AddParameter(command, "$asset_uid", asset.AssetUid);
        AddParameter(command, "$current_path", asset.CurrentPath);
        AddParameter(command, "$content_hash", asset.ContentHash ?? string.Empty);

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        return ReadDocument(reader);
    }

    private static AssetDescriptionDocument ReadDocument(SqliteDataReader reader)
    {
        return new AssetDescriptionDocument(
            reader.GetString(0),
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
        AssetDescriptionDocument document,
        CancellationToken ct)
    {
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
                'described',
                'pending',
                $created_at,
                $updated_at
            )
            ON CONFLICT(asset_uid) DO UPDATE SET
                metadata_status = excluded.metadata_status,
                updated_at = excluded.updated_at;
            """;

        AddParameter(command, "$asset_uid", document.AssetUid);
        AddParameter(command, "$created_at", document.GeneratedAt.ToString("O"));
        AddParameter(command, "$updated_at", document.GeneratedAt.ToString("O"));
        await command.ExecuteNonQueryAsync(ct);
    }
}
