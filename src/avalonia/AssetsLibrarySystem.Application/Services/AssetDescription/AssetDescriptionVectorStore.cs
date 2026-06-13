using System;
using System.Collections.Generic;
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

    public async Task ReplaceForAssetAsync(long assetId, string embeddingModel, IReadOnlyList<AssetDescriptionVectorDocument> documents, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(documents);

        await AssetDatabase.EnsureSchemaAsync(ct);
        await WriteQueue.EnqueueAsync(async token =>
        {
            await using var connection = await AssetDatabase.OpenConnectionAsync(token);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(token).ConfigureAwait(false);

            await DeleteVectorsAsync(connection, transaction, assetId, embeddingModel, token).ConfigureAwait(false);

            AssetDescriptionVectorDocument? lastDocument = null;
            foreach (var document in documents)
            {
                lastDocument = document;
                await InsertVectorAsync(connection, transaction, document, token).ConfigureAwait(false);
            }

            if (lastDocument is not null)
            {
                await UpdateAssetMetadataAsync(connection, transaction, lastDocument, token).ConfigureAwait(false);
            }

            await transaction.CommitAsync(token).ConfigureAwait(false);
        }, ct);
    }

    public async Task<IReadOnlyList<AssetDescriptionVectorDocument>> ListByAssetIdAsync(long assetId, CancellationToken ct = default)
    {
        await using var connection = await AssetDatabase.OpenConnectionAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                v.asset_id,
                a.asset_uid,
                angle_type,
                embedding_model,
                vector_dim,
                vector_blob,
                vectorized_at,
                content_hash
            FROM asset_description_vectors AS v
            INNER JOIN assets AS a ON a.id = v.asset_id
            WHERE v.asset_id = $asset_id
            ORDER BY v.angle_type;
            """;
        AddParameter(command, "$asset_id", assetId);

        await using var reader = await command.ExecuteReaderAsync(ct);
        var documents = new List<AssetDescriptionVectorDocument>();
        while (await reader.ReadAsync(ct))
        {
            var vector = DeserializeVector(reader.GetFieldValue<byte[]>(5));
            documents.Add(new AssetDescriptionVectorDocument(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetInt32(4),
                vector,
                DateTimeOffset.Parse(reader.GetString(6)),
                reader.IsDBNull(7) ? null : reader.GetString(7)));
        }

        return documents;
    }

    public async Task<bool> DeleteAsync(long assetId, CancellationToken ct = default)
    {
        await AssetDatabase.EnsureSchemaAsync(ct);
        return await WriteQueue.EnqueueAsync(async token =>
        {
            await using var connection = await AssetDatabase.OpenConnectionAsync(token);

            await using var command = connection.CreateCommand();
            command.CommandText = """
                DELETE FROM asset_description_vectors
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

    public async Task<bool> NeedsVectorizationAsync(
        long assetId,
        string embeddingModel,
        string? descriptionContentHash = null,
        DateTimeOffset? descriptionGeneratedAt = null,
        CancellationToken ct = default)
    {
        await using var connection = await AssetDatabase.OpenConnectionAsync(ct);

        // 查询该素材的最新向量的 content_hash 和 vectorized_at
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT content_hash, vectorized_at
            FROM asset_description_vectors
            WHERE asset_id = $asset_id
              AND embedding_model = $embedding_model
            ORDER BY vectorized_at DESC
            LIMIT 1;
            """;
        AddParameter(command, "$asset_id", assetId);
        AddParameter(command, "$embedding_model", embeddingModel);

        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            // 无向量记录 → 需要向量化
            return true;
        }

        var vectorContentHash = reader.IsDBNull(0) ? null : reader.GetString(0);
        var maxVectorizedAt = DateTimeOffset.Parse(reader.GetString(1));

        // 描述生成时间晚于向量化时间 → 描述已更新，需要重新向量化
        if (descriptionGeneratedAt.HasValue && descriptionGeneratedAt.Value > maxVectorizedAt)
        {
            return true;
        }

        // 双方 content_hash 均可用 → 精确比较
        if (!string.IsNullOrEmpty(descriptionContentHash) && !string.IsNullOrEmpty(vectorContentHash))
        {
            return !string.Equals(descriptionContentHash, vectorContentHash, StringComparison.OrdinalIgnoreCase);
        }

        // 描述生成时间 <= 向量化时间，且未触发上述任何条件 → 向量已是最新
        if (descriptionGeneratedAt.HasValue && descriptionGeneratedAt.Value <= maxVectorizedAt)
        {
            return false;
        }

        // 信息不足 → 保守地认为需要向量化
        return true;
    }

    public async Task MarkAsIndexedAsync(long assetId, CancellationToken ct = default)
    {
        await AssetDatabase.EnsureSchemaAsync(ct);
        await WriteQueue.EnqueueAsync(async token =>
        {
            await using var connection = await AssetDatabase.OpenConnectionAsync(token);

            await using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE asset_metadata
                SET vector_state = 'indexed',
                    updated_at = $updated_at
                WHERE asset_id = $asset_id
                  AND vector_state <> 'indexed';
                """;
            AddParameter(command, "$asset_id", assetId);
            AddParameter(command, "$updated_at", DateTimeOffset.UtcNow.ToString("O"));
            await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }, ct);
    }

    private static async Task InsertVectorAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        AssetDescriptionVectorDocument document,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO asset_description_vectors (
                asset_id,
                angle_type,
                embedding_model,
                vector_dim,
                vector_blob,
                vectorized_at,
                content_hash
            )
            VALUES (
                $asset_id,
                $angle_type,
                $embedding_model,
                $vector_dim,
                $vector_blob,
                $vectorized_at,
                $content_hash
            )
            ON CONFLICT(asset_id, angle_type, embedding_model) DO UPDATE SET
                vector_dim = excluded.vector_dim,
                vector_blob = excluded.vector_blob,
                vectorized_at = excluded.vectorized_at,
                content_hash = excluded.content_hash;
            """;

        AddParameter(command, "$asset_id", document.AssetId);
        AddParameter(command, "$angle_type", string.IsNullOrWhiteSpace(document.AngleType) ? AssetDescriptionVectorDocument.DefaultAngleType : document.AngleType.Trim());
        AddParameter(command, "$embedding_model", document.EmbeddingModel);
        AddParameter(command, "$vector_dim", document.VectorDim);
        command.Parameters.Add(new SqliteParameter("$vector_blob", SqliteType.Blob)
        {
            Value = SerializeVector(document.Vector),
        });
        AddParameter(command, "$vectorized_at", document.VectorizedAt.ToString("O"));
        AddParameter(command, "$content_hash", (object?)document.ContentHash ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task DeleteVectorsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long assetId,
        string embeddingModel,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DELETE FROM asset_description_vectors
            WHERE asset_id = $asset_id
              AND embedding_model = $embedding_model;
            """;
        AddParameter(command, "$asset_id", assetId);
        AddParameter(command, "$embedding_model", embeddingModel);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
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
        SqliteTransaction transaction,
        AssetDescriptionVectorDocument document,
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
                created_at,
                updated_at
            )
            VALUES (
                $asset_id,
                '[]',
                'described',
                'indexed',
                $created_at,
                $updated_at
            )
            ON CONFLICT(asset_id) DO UPDATE SET
                vector_state = excluded.vector_state,
                updated_at = excluded.updated_at;
            """;

        AddParameter(command, "$asset_id", document.AssetId);
        AddParameter(command, "$created_at", document.VectorizedAt.ToString("O"));
        AddParameter(command, "$updated_at", document.VectorizedAt.ToString("O"));
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
            SET vector_state = 'pending',
                updated_at = $updated_at
            WHERE asset_id = $asset_id;
            """;

        AddParameter(command, "$asset_id", assetId);
        AddParameter(command, "$updated_at", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}
