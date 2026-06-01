using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AssetsLibrarySystem.Avalonia.Models;
using Microsoft.Data.Sqlite;

namespace AssetsLibrarySystem.Avalonia.Services.AssetDescription;

public sealed class AssetDescriptionStore : IAssetDescriptionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public string DatabasePath { get; }

    public AssetDescriptionStore()
    {
        DatabasePath = AssetDescriptionPathHelper.BuildDatabasePath();
    }

    public async Task SaveAsync(AssetDescriptionDocument document, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);

        await using var connection = CreateConnection();
        await connection.OpenAsync(ct);

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
                system_prompt
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
                $system_prompt
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
                system_prompt = excluded.system_prompt;
            """;

        AddParameter(command, "$asset_id", document.AssetId);
        AddParameter(command, "$asset_name", document.AssetName);
        AddParameter(command, "$asset_type", document.AssetType);
        AddParameter(command, "$asset_path", document.AssetPath);
        AddParameter(command, "$store_path", document.StorePath);
        AddParameter(command, "$description", document.Description);
        AddParameter(command, "$backend_endpoint", document.BackendEndpoint);
        AddParameter(command, "$mode", document.Mode);
        AddParameter(command, "$generated_at", document.GeneratedAt.ToString("O"));
        AddParameter(command, "$token_usage_json", SerializeTokenUsage(document.TokenUsage));
        AddParameter(command, "$prompt", (object?)document.Prompt ?? DBNull.Value);
        AddParameter(command, "$system_prompt", (object?)document.SystemPrompt ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<AssetDescriptionDocument?> TryGetAsync(string assetId, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);

        await using var connection = CreateConnection();
        await connection.OpenAsync(ct);

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
                system_prompt
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
            reader.IsDBNull(11) ? null : reader.GetString(11));
    }

    private async Task EnsureSchemaAsync(CancellationToken ct)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS asset_descriptions (
                asset_id TEXT PRIMARY KEY,
                asset_name TEXT NOT NULL,
                asset_type TEXT NOT NULL,
                asset_path TEXT NOT NULL,
                store_path TEXT NOT NULL,
                description TEXT NOT NULL,
                backend_endpoint TEXT NOT NULL,
                mode TEXT NOT NULL,
                generated_at TEXT NOT NULL,
                token_usage_json TEXT NULL,
                prompt TEXT NULL,
                system_prompt TEXT NULL
            );
            """;

        await command.ExecuteNonQueryAsync(ct);
    }

    private SqliteConnection CreateConnection()
    {
        return new SqliteConnection($"Data Source={DatabasePath}");
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
}
