using System;
using System.Threading;
using System.Threading.Tasks;
using AssetsLibrarySystem.Application.Models;
using AssetsLibrarySystem.Application.Services.Infrastructure;
using Microsoft.Data.Sqlite;

namespace AssetsLibrarySystem.Application.Services.AssetDescription;

public sealed class AssetDescriptionVectorStore : IAssetDescriptionVectorStore
{
    private IDatabaseWriteQueue WriteQueue { get; }
    private IAssetDatabase AssetDatabase { get; }

    public AssetDescriptionVectorStore(IDatabaseWriteQueue writeQueue, IAssetDatabase assetDatabase)
    {
        WriteQueue = writeQueue;
        AssetDatabase = assetDatabase;
    }

    public string DatabasePath => AssetDatabase.DatabasePath;

    public async Task SaveAsync(AssetDescriptionVectorDocument document, CancellationToken ct = default)
    {
        await AssetDatabase.EnsureSchemaAsync(ct);
        await WriteQueue.EnqueueAsync(async token =>
        {
            await using var connection = await AssetDatabase.OpenConnectionAsync(token);

            await using var command = connection.CreateCommand();
            command.CommandText = """
            INSERT INTO asset_description_vectors (
                asset_id,
                description_store_path,
                embedding_model,
                vector_dim,
                vector_blob,
                vectorized_at,
                content_hash
            )
            VALUES (
                $asset_id,
                $description_store_path,
                $embedding_model,
                $vector_dim,
                $vector_blob,
                $vectorized_at,
                $content_hash
            )
            ON CONFLICT(asset_id) DO UPDATE SET
                description_store_path = excluded.description_store_path,
                embedding_model = excluded.embedding_model,
                vector_dim = excluded.vector_dim,
                vector_blob = excluded.vector_blob,
                vectorized_at = excluded.vectorized_at,
                content_hash = excluded.content_hash;
            """;

            AddParameter(command, "$asset_id", document.AssetUid);
            AddParameter(command, "$description_store_path", document.DescriptionStorePath);
            AddParameter(command, "$embedding_model", document.EmbeddingModel);
            AddParameter(command, "$vector_dim", document.VectorDim);
            command.Parameters.Add(new SqliteParameter("$vector_blob", SqliteType.Blob)
            {
                Value = SerializeVector(document.Vector),
            });
            AddParameter(command, "$vectorized_at", document.VectorizedAt.ToString("O"));
            AddParameter(command, "$content_hash", (object?)document.ContentHash ?? DBNull.Value);

            await command.ExecuteNonQueryAsync(token);
            await UpdateAssetMetadataAsync(connection, document, token);
        }, ct);
    }

    public async Task<AssetDescriptionVectorDocument?> TryGetAsync(string assetId, CancellationToken ct = default)
    {
        await using var connection = await AssetDatabase.OpenConnectionAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                asset_id,
                description_store_path,
                embedding_model,
                vector_dim,
                vector_blob,
                vectorized_at,
                content_hash
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

        var vector = DeserializeVector(reader.GetFieldValue<byte[]>(4));
        return new AssetDescriptionVectorDocument(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetInt32(3),
            vector,
            DateTimeOffset.Parse(reader.GetString(5)),
            reader.IsDBNull(6) ? null : reader.GetString(6));
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

    private static async Task UpdateAssetMetadataAsync(
        SqliteConnection connection,
        AssetDescriptionVectorDocument document,
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
                'indexed',
                $created_at,
                $updated_at
            )
            ON CONFLICT(asset_uid) DO UPDATE SET
                vector_state = excluded.vector_state,
                updated_at = excluded.updated_at;
            """;

        AddParameter(command, "$asset_uid", document.AssetUid);
        AddParameter(command, "$created_at", document.VectorizedAt.ToString("O"));
        AddParameter(command, "$updated_at", document.VectorizedAt.ToString("O"));
        await command.ExecuteNonQueryAsync(ct);
    }
}
