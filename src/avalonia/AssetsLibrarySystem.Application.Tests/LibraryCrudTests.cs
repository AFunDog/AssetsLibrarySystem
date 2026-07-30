using AssetsLibrarySystem.Application.Models;
using AssetsLibrarySystem.Application.Services.AssetLibrary;
using AssetsLibrarySystem.Application.Services.Infrastructure;
using Microsoft.Data.Sqlite;
using Xunit;

namespace AssetsLibrarySystem.Application.Tests;

public sealed class LibraryCrudTests : IAsyncDisposable
{
    private string DatabasePath { get; } = Path.Combine(Path.GetTempPath(), $"library-crud-{Guid.NewGuid():N}.db");
    private DatabaseWriteQueue WriteQueue { get; } = new();

    [Fact]
    public async Task DeleteLibraryAsync_RemovesLibraryAndCascadesAssets()
    {
        var database = new CrudTestDatabase(DatabasePath);
        await database.EnsureSchemaAsync();
        await SeedLibraryWithAssetAsync(database);

        var service = new AssetLibraryService(WriteQueue, database);
        await service.DeleteLibraryAsync(1);

        // 验证素材库已删除
        await using var connection = await database.OpenConnectionAsync();
        await using var cmd1 = connection.CreateCommand();
        cmd1.CommandText = "SELECT COUNT(*) FROM libraries WHERE id = 1;";
        Assert.Equal(0L, (await cmd1.ExecuteScalarAsync())!);

        // 验证关联素材已级联删除
        await using var cmd2 = connection.CreateCommand();
        cmd2.CommandText = "SELECT COUNT(*) FROM assets WHERE id = 1;";
        Assert.Equal(0L, (await cmd2.ExecuteScalarAsync())!);

        // 验证关联元数据已级联删除
        await using var cmd3 = connection.CreateCommand();
        cmd3.CommandText = "SELECT COUNT(*) FROM asset_metadata WHERE asset_id = 1;";
        Assert.Equal(0L, (await cmd3.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task DeleteLibraryAsync_ThrowsIfNotExists()
    {
        var database = new CrudTestDatabase(DatabasePath);
        await database.EnsureSchemaAsync();
        var service = new AssetLibraryService(WriteQueue, database);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DeleteLibraryAsync(999));
        Assert.Contains("不存在", ex.Message);
    }

    [Fact]
    public async Task DeleteAssetAsync_RemovesAssetAndCascadesMetadata()
    {
        var database = new CrudTestDatabase(DatabasePath);
        await database.EnsureSchemaAsync();
        await SeedLibraryWithAssetAsync(database);

        var service = new AssetLibraryService(WriteQueue, database);
        await service.DeleteAssetAsync(1);

        await using var connection = await database.OpenConnectionAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM assets WHERE id = 1;";
        Assert.Equal(0L, (await cmd.ExecuteScalarAsync())!);

        // 验证描述已级联删除
        await using var cmd2 = connection.CreateCommand();
        cmd2.CommandText = "SELECT COUNT(*) FROM asset_descriptions WHERE asset_id = 1;";
        Assert.Equal(0L, (await cmd2.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task UpdateAssetTagsAsync_PersistsTags()
    {
        var database = new CrudTestDatabase(DatabasePath);
        await database.EnsureSchemaAsync();
        await SeedLibraryWithAssetAsync(database);

        var service = new AssetLibraryService(WriteQueue, database);
        await service.UpdateAssetTagsAsync(1, ["恐怖", "氛围", "追逐"]);

        await using var connection = await database.OpenConnectionAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT tags_json FROM asset_metadata WHERE asset_id = 1;";
        var tagsJson = (string)(await cmd.ExecuteScalarAsync())!;
        var deserialized = System.Text.Json.JsonSerializer.Deserialize<string[]>(tagsJson);
        Assert.NotNull(deserialized);
        Assert.Contains("恐怖", deserialized);
        Assert.Contains("氛围", deserialized);
        Assert.Contains("追逐", deserialized);
    }

    [Fact]
    public async Task UpdateAssetNameAsync_UpdatesBothTables()
    {
        var database = new CrudTestDatabase(DatabasePath);
        await database.EnsureSchemaAsync();
        await SeedLibraryWithAssetAsync(database);

        var service = new AssetLibraryService(WriteQueue, database);
        await service.UpdateAssetNameAsync(1, "新名称.mp3");

        await using var connection = await database.OpenConnectionAsync();

        // 验证 assets 表
        await using var cmd1 = connection.CreateCommand();
        cmd1.CommandText = "SELECT asset_name FROM assets WHERE id = 1;";
        Assert.Equal("新名称.mp3", (string)(await cmd1.ExecuteScalarAsync())!);

        // 验证 asset_descriptions 表
        await using var cmd2 = connection.CreateCommand();
        cmd2.CommandText = "SELECT asset_name FROM asset_descriptions WHERE asset_id = 1;";
        Assert.Equal("新名称.mp3", (string)(await cmd2.ExecuteScalarAsync())!);
    }

    public async ValueTask DisposeAsync()
    {
        await WriteQueue.DisposeAsync();
        SqliteConnection.ClearAllPools();
        File.Delete(DatabasePath);
    }

    private static async Task SeedLibraryWithAssetAsync(IAssetDatabase database)
    {
        await using var connection = await database.OpenConnectionAsync();
        await using var cmd1 = connection.CreateCommand();
        cmd1.CommandText = """
            INSERT INTO libraries (id, name, root_path, created_at, updated_at)
            VALUES (1, '测试库', '/tmp/test', '2024-01-01T00:00:00Z', '2024-01-01T00:00:00Z');
            """;
        await cmd1.ExecuteNonQueryAsync();

        await using var cmd2 = connection.CreateCommand();
        cmd2.CommandText = """
            INSERT INTO assets (id, asset_uid, library_id, asset_name, asset_type,
                                current_path, content_hash, observed_hash,
                                file_size, modified_time_utc, status,
                                created_at, updated_at, created_by)
            VALUES (1, 'uid_test', 1, 'test.mp3', '音频',
                    '/tmp/test/test.mp3', 'hash123', 'hash123',
                    1024, '2024-01-01T00:00:00Z', 'ok',
                    '2024-01-01T00:00:00Z', '2024-01-01T00:00:00Z', 'test');
            """;
        await cmd2.ExecuteNonQueryAsync();

        await using var cmd3 = connection.CreateCommand();
        cmd3.CommandText = """
            INSERT INTO asset_metadata (asset_id, tags_json, metadata_status, vector_state, created_at, updated_at)
            VALUES (1, '[]', 'described', 'indexed', '2024-01-01T00:00:00Z', '2024-01-01T00:00:00Z');
            """;
        await cmd3.ExecuteNonQueryAsync();

        await using var cmd4 = connection.CreateCommand();
        cmd4.CommandText = """
            INSERT INTO asset_descriptions (asset_id, asset_name, asset_type, asset_path,
                                            description, backend_endpoint, mode, generated_at)
            VALUES (1, 'test.mp3', '音频', '/tmp/test/test.mp3',
                    '测试描述', 'http://localhost:8000', 'mock', '2024-01-01T00:00:00Z');
            """;
        await cmd4.ExecuteNonQueryAsync();
    }

    private sealed class CrudTestDatabase(string databasePath) : IAssetDatabase
    {
        public string DatabasePath { get; } = databasePath;

        public async Task EnsureSchemaAsync(CancellationToken ct = default)
        {
            await using var connection = await OpenConnectionCoreAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                PRAGMA foreign_keys = ON;
                CREATE TABLE IF NOT EXISTS libraries (
                    id INTEGER PRIMARY KEY,
                    name TEXT NOT NULL,
                    root_path TEXT NOT NULL,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS assets (
                    id INTEGER PRIMARY KEY,
                    asset_uid TEXT NOT NULL,
                    library_id INTEGER NOT NULL REFERENCES libraries(id) ON DELETE CASCADE,
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
                CREATE TABLE IF NOT EXISTS asset_metadata (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    asset_id INTEGER NOT NULL REFERENCES assets(id) ON DELETE CASCADE,
                    tags_json TEXT NOT NULL DEFAULT '[]',
                    metadata_status TEXT NOT NULL,
                    vector_state TEXT NOT NULL DEFAULT 'pending',
                    subtype TEXT NULL,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL,
                    UNIQUE(asset_id)
                );
                CREATE TABLE IF NOT EXISTS asset_descriptions (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    asset_id INTEGER NOT NULL REFERENCES assets(id) ON DELETE CASCADE,
                    asset_name TEXT NOT NULL,
                    asset_type TEXT NOT NULL,
                    asset_path TEXT NOT NULL,
                    description TEXT NOT NULL,
                    backend_endpoint TEXT NOT NULL,
                    mode TEXT NOT NULL,
                    generated_at TEXT NOT NULL,
                    token_usage_json TEXT NULL,
                    prompt TEXT NULL,
                    system_prompt TEXT NULL,
                    content_hash TEXT NULL,
                    metadata_status TEXT NOT NULL DEFAULT 'ready',
                    UNIQUE(asset_id)
                );
                """;
            await command.ExecuteNonQueryAsync(ct);
        }

        public async Task<SqliteConnection> OpenConnectionAsync(CancellationToken ct = default)
        {
            await EnsureSchemaAsync(ct);
            return await OpenConnectionCoreAsync(ct);
        }

        public SqliteConnection OpenConnection()
        {
            EnsureSchemaAsync().GetAwaiter().GetResult();
            var connection = new SqliteConnection($"Data Source={DatabasePath}");
            connection.Open();
            return connection;
        }

        public Task UpdateSubtypeAsync(long assetId, string subtype, CancellationToken ct = default)
            => Task.CompletedTask;

        private async Task<SqliteConnection> OpenConnectionCoreAsync(CancellationToken ct)
        {
            var connection = new SqliteConnection($"Data Source={DatabasePath}");
            await connection.OpenAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA foreign_keys = ON;";
            await command.ExecuteNonQueryAsync(ct);
            return connection;
        }
    }
}