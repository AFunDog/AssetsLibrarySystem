using AssetsLibrarySystem.Application.Services.AssetSearch;
using AssetsLibrarySystem.Application.Services.Infrastructure;
using Microsoft.Data.Sqlite;
using Xunit;

namespace AssetsLibrarySystem.Application.Tests;

/// <summary>
/// VectorRecordRepository 版本化缓存：同一版本内重复加载返回缓存；
/// COUNT/MAX(vectorized_at) 变化后自动重载。
/// </summary>
public sealed class VectorRecordRepositoryCacheTests : IAsyncDisposable
{
    private string DatabasePath { get; } = Path.Combine(Path.GetTempPath(), $"vector-repo-cache-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task LoadAsync_ReusesCache_UntilVersionChanges()
    {
        // 清理静态缓存，避免测试间串扰
        VectorRecordRepository.ResetCacheForTesting();

        var database = new VectorCacheTestDatabase(DatabasePath);
        var repository = new VectorRecordRepository(database);

        // 空库首次加载
        var first = await repository.LoadAsync("model-a");
        Assert.Empty(first);

        // 写入一条向量（版本变化）→ 重载命中
        await InsertVectorAsync(database, 1);
        var second = await repository.LoadAsync("model-a");
        Assert.Single(second);

        // 数据未变 → 缓存命中（同一引用，无 DB 查询负担）
        var third = await repository.LoadAsync("model-a");
        Assert.Same(second, third);

        // 再写入一条（版本变化）→ 重载
        await InsertVectorAsync(database, 2);
        var fourth = await repository.LoadAsync("model-a");
        Assert.Equal(2, fourth.Count);
    }

    [Fact]
    public async Task LoadAsync_DifferentModels_DoNotShareCache()
    {
        VectorRecordRepository.ResetCacheForTesting();
        var database = new VectorCacheTestDatabase(DatabasePath);
        var repository = new VectorRecordRepository(database);

        await InsertVectorAsync(database, 1);
        var modelA = await repository.LoadAsync("model-a");
        var modelB = await repository.LoadAsync("model-b");

        Assert.Single(modelA);
        Assert.Empty(modelB);
    }

    private static async Task InsertVectorAsync(IAssetDatabase database, int assetId)
    {
        await database.EnsureSchemaAsync(CancellationToken.None);
        await using var connection = await database.OpenConnectionAsync(CancellationToken.None);
        await using var insertAsset = connection.CreateCommand();
        insertAsset.CommandText = """
            INSERT INTO assets (id, asset_uid, asset_name, asset_type, current_path)
            VALUES ($id, $uid, '测试素材', '图片', '/tmp/asset.png');
            """;
        insertAsset.Parameters.AddWithValue("$id", assetId);
        insertAsset.Parameters.AddWithValue("$uid", $"asset-{assetId}");
        await insertAsset.ExecuteNonQueryAsync(CancellationToken.None);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO asset_description_vectors
                (asset_id, angle_type, embedding_model, vector_dim, vector_blob, vectorized_at, content_hash)
            VALUES
                ($asset_id, '整体', 'model-a', 2, $vector_blob, $vectorized_at, 'hash-1');
            """;
        command.Parameters.AddWithValue("$asset_id", assetId);
        var floats = new[] { 1.0f, 0.5f };
        var blob = new byte[floats.Length * sizeof(float)];
        Buffer.BlockCopy(floats, 0, blob, 0, blob.Length);
        command.Parameters.AddWithValue("$vector_blob", blob);
        command.Parameters.AddWithValue("$vectorized_at", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    public ValueTask DisposeAsync()
    {
        try
        {
            File.Delete(DatabasePath);
            File.Delete(DatabasePath + "-wal");
            File.Delete(DatabasePath + "-shm");
        }
        catch (IOException)
        {
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>最小 IAssetDatabase：只建向量表，供版本缓存测试使用。</summary>
    private sealed class VectorCacheTestDatabase(string databasePath) : IAssetDatabase
    {
        public string DatabasePath { get; } = databasePath;

        public Task EnsureSchemaAsync(CancellationToken ct = default)
        {
            return OpenConnectionCoreAsync(ct).ContinueWith(
                t =>
                {
                    using var command = t.Result.CreateCommand();
                    command.CommandText = """
                        PRAGMA foreign_keys = ON;
                        CREATE TABLE IF NOT EXISTS assets (
                            id INTEGER PRIMARY KEY AUTOINCREMENT,
                            asset_uid TEXT NOT NULL,
                            asset_name TEXT NOT NULL,
                            asset_type TEXT NOT NULL DEFAULT '',
                            current_path TEXT NOT NULL DEFAULT ''
                        );
                        CREATE TABLE IF NOT EXISTS asset_descriptions (
                            id INTEGER PRIMARY KEY AUTOINCREMENT,
                            asset_id INTEGER NOT NULL,
                            asset_name TEXT NOT NULL DEFAULT '',
                            asset_type TEXT NOT NULL DEFAULT '',
                            asset_path TEXT NOT NULL DEFAULT '',
                            description TEXT NULL,
                            generated_at TEXT NULL
                        );
                        CREATE TABLE IF NOT EXISTS asset_metadata (
                            asset_id INTEGER PRIMARY KEY,
                            tags_json TEXT NOT NULL DEFAULT '[]'
                        );
                        CREATE TABLE IF NOT EXISTS asset_description_vectors (
                            id INTEGER PRIMARY KEY AUTOINCREMENT,
                            asset_id INTEGER NOT NULL,
                            angle_type TEXT NOT NULL DEFAULT '整体',
                            embedding_model TEXT NOT NULL,
                            vector_dim INTEGER NOT NULL,
                            vector_blob BLOB NOT NULL,
                            vectorized_at TEXT NOT NULL,
                            content_hash TEXT NULL
                        );
                        CREATE INDEX IF NOT EXISTS ix_asset_description_vectors_embedding_model
                            ON asset_description_vectors(embedding_model);
                        """;
                    command.ExecuteNonQuery();
                    t.Result.Dispose();
                },
                ct);
        }

        public async Task<SqliteConnection> OpenConnectionAsync(CancellationToken ct = default)
        {
            var connection = new SqliteConnection($"Data Source={DatabasePath}");
            await connection.OpenAsync(ct);
            return connection;
        }

        public SqliteConnection OpenConnection()
        {
            var connection = new SqliteConnection($"Data Source={DatabasePath}");
            connection.Open();
            return connection;
        }

        public Task UpdateSubtypeAsync(long assetId, string subtype, CancellationToken ct = default)
        {
            return Task.CompletedTask;
        }

        private async Task<SqliteConnection> OpenConnectionCoreAsync(CancellationToken ct)
        {
            var connection = new SqliteConnection($"Data Source={DatabasePath}");
            await connection.OpenAsync(ct);
            return connection;
        }
    }
}
