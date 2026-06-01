using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AssetsLibrarySystem.Avalonia.Infrastructure;
using AssetsLibrarySystem.Avalonia.Models;
using Microsoft.Data.Sqlite;

namespace AssetsLibrarySystem.Avalonia.Services.AssetDescription;

public sealed class AssetDescriptionVectorStore : IAssetDescriptionVectorStore
{
    public string DatabasePath { get; }

    public AssetDescriptionVectorStore()
    {
        DatabasePath = SharedDataPathHelper.GetDataFilePath("asset_descriptions.db");
    }

    public async Task SaveAsync(AssetDescriptionVectorDocument document, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);

        await using var connection = CreateConnection();
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO asset_description_vectors (
                asset_id,
                asset_name,
                asset_type,
                asset_path,
                description,
                description_store_path,
                embedding_model,
                vector_dim,
                vector_blob,
                vectorized_at
            )
            VALUES (
                $asset_id,
                $asset_name,
                $asset_type,
                $asset_path,
                $description,
                $description_store_path,
                $embedding_model,
                $vector_dim,
                $vector_blob,
                $vectorized_at
            )
            ON CONFLICT(asset_id) DO UPDATE SET
                asset_name = excluded.asset_name,
                asset_type = excluded.asset_type,
                asset_path = excluded.asset_path,
                description = excluded.description,
                description_store_path = excluded.description_store_path,
                embedding_model = excluded.embedding_model,
                vector_dim = excluded.vector_dim,
                vector_blob = excluded.vector_blob,
                vectorized_at = excluded.vectorized_at;
            """;

        AddParameter(command, "$asset_id", document.AssetId);
        AddParameter(command, "$asset_name", document.AssetName);
        AddParameter(command, "$asset_type", document.AssetType);
        AddParameter(command, "$asset_path", document.AssetPath);
        AddParameter(command, "$description", document.Description);
        AddParameter(command, "$description_store_path", document.DescriptionStorePath);
        AddParameter(command, "$embedding_model", document.EmbeddingModel);
        AddParameter(command, "$vector_dim", document.VectorDim);
        command.Parameters.Add(new SqliteParameter("$vector_blob", SqliteType.Blob)
        {
            Value = SerializeVector(document.Vector),
        });
        AddParameter(command, "$vectorized_at", document.VectorizedAt.ToString("O"));

        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<AssetDescriptionVectorDocument?> TryGetAsync(string assetId, CancellationToken ct = default)
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
                description,
                description_store_path,
                embedding_model,
                vector_dim,
                vector_blob,
                vectorized_at
            FROM asset_description_vectors
            WHERE asset_id = $asset_id
            LIMIT 1;
            """;
        AddParameter(command, "$asset_id", assetId);

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        var vector = DeserializeVector(reader.GetFieldValue<byte[]>(8));
        return new AssetDescriptionVectorDocument(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetInt32(7),
            vector,
            DateTimeOffset.Parse(reader.GetString(9)));
    }

    private async Task EnsureSchemaAsync(CancellationToken ct)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS asset_description_vectors (
                asset_id TEXT PRIMARY KEY,
                asset_name TEXT NOT NULL,
                asset_type TEXT NOT NULL,
                asset_path TEXT NOT NULL,
                description TEXT NOT NULL,
                description_store_path TEXT NOT NULL,
                embedding_model TEXT NOT NULL,
                vector_dim INTEGER NOT NULL,
                vector_blob BLOB NOT NULL,
                vectorized_at TEXT NOT NULL
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

    private static byte[] SerializeVector(float[] vector)
    {
        var bytes = new byte[vector.Length * sizeof(float)];
        Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static float[] DeserializeVector(byte[] bytes)
    {
        var vector = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, vector, 0, bytes.Length);
        return vector;
    }
}
