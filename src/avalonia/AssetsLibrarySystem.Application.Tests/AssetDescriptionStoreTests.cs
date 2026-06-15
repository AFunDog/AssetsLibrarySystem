using AssetsLibrarySystem.Application.Models;
using AssetsLibrarySystem.Application.Services.AssetDescription;
using AssetsLibrarySystem.Application.Services.Infrastructure;
using Microsoft.Data.Sqlite;
using Xunit;

namespace AssetsLibrarySystem.Application.Tests;

public sealed class AssetDescriptionStoreTests : IAsyncDisposable
{
    private string DatabasePath { get; } = Path.Combine(Path.GetTempPath(), $"asset-description-store-{Guid.NewGuid():N}.db");
    private DatabaseWriteQueue WriteQueue { get; } = new();

    [Fact]
    public async Task SaveAsync_InsertsAndUpdatesDescriptionAndMetadata()
    {
        var database = new TestAssetDatabase(DatabasePath);
        await database.EnsureSchemaAsync();
        await SeedAssetAsync(database);
        var store = new AssetDescriptionStore(WriteQueue, database);
        var generatedAt = DateTimeOffset.UtcNow;

        await store.SaveAsync(CreateDocument("第一次描述", generatedAt));
        await store.SaveAsync(CreateDocument("更新后的描述", generatedAt.AddSeconds(1)));

        var saved = await store.TryGetAsync(1);
        Assert.NotNull(saved);
        Assert.Equal("更新后的描述", saved.Description);

        await using var connection = await database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT metadata_status, vector_state FROM asset_metadata WHERE asset_id = 1;";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("described", reader.GetString(0));
        Assert.Equal("pending", reader.GetString(1));
    }

    public async ValueTask DisposeAsync()
    {
        await WriteQueue.DisposeAsync();
        SqliteConnection.ClearAllPools();
        File.Delete(DatabasePath);
    }

    private static AssetDescriptionDocument CreateDocument(string description, DateTimeOffset generatedAt)
    {
        return new AssetDescriptionDocument(
            AssetId: 1,
            AssetUid: "asset_test",
            AssetName: "sample.mp3",
            AssetType: "音频",
            CurrentPath: @"D:\Data\sample.mp3",
            Description: description,
            BackendEndpoint: "http://127.0.0.1:8000",
            Mode: "live",
            GeneratedAt: generatedAt,
            TokenUsage: null,
            Prompt: null,
            SystemPrompt: null,
            ContentHash: "hash",
            MetadataStatus: "ready");
    }

    private static async Task SeedAssetAsync(IAssetDatabase database)
    {
        await using var connection = await database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO assets (id, asset_uid)
            VALUES (1, 'asset_test');
            """;
        await command.ExecuteNonQueryAsync();
    }

    private sealed class TestAssetDatabase(string databasePath) : IAssetDatabase
    {
        public string DatabasePath { get; } = databasePath;

        public async Task EnsureSchemaAsync(CancellationToken ct = default)
        {
            await using var connection = await OpenConnectionCoreAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                PRAGMA foreign_keys = ON;
                CREATE TABLE IF NOT EXISTS assets (
                    id INTEGER PRIMARY KEY,
                    asset_uid TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS asset_metadata (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    asset_id INTEGER NOT NULL REFERENCES assets(id) ON DELETE CASCADE,
                    tags_json TEXT NOT NULL DEFAULT '[]',
                    metadata_status TEXT NOT NULL,
                    vector_state TEXT NOT NULL DEFAULT 'pending',
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
