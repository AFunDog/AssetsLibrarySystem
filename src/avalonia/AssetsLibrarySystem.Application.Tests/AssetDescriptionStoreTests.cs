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

    [Fact]
    public async Task SaveAsync_WithTokenUsage_AppendsUsageLog()
    {
        var database = new TestAssetDatabase(DatabasePath);
        await database.EnsureSchemaAsync();
        await SeedAssetAsync(database);
        var store = new AssetDescriptionStore(WriteQueue, database);

        var usage = new AssetDescriptionTokenUsage(1000, 100, 1100, null, null, null, null, null, null, 0.0123);
        await store.SaveAsync(CreateDocument("带用量", DateTimeOffset.UtcNow) with { TokenUsage = usage });

        await using var connection = await database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT asset_id, asset_name, mode, input_tokens, output_tokens, total_tokens, estimated_cost_cny
            FROM asset_token_usage_log;
            """;
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(1, reader.GetInt64(0));
        Assert.Equal("sample.mp3", reader.GetString(1));
        Assert.Equal("live", reader.GetString(2));
        Assert.Equal(1000, reader.GetInt32(3));
        Assert.Equal(100, reader.GetInt32(4));
        Assert.Equal(1100, reader.GetInt32(5));
        Assert.Equal(0.0123, reader.GetDouble(6), 4);
        Assert.False(await reader.ReadAsync());
    }

    [Fact]
    public async Task SaveAsync_WithoutTokenUsage_SkipsUsageLog()
    {
        var database = new TestAssetDatabase(DatabasePath);
        await database.EnsureSchemaAsync();
        await SeedAssetAsync(database);
        var store = new AssetDescriptionStore(WriteQueue, database);

        await store.SaveAsync(CreateDocument("无用量", DateTimeOffset.UtcNow));

        await using var connection = await database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM asset_token_usage_log;";
        var count = (long)(await command.ExecuteScalarAsync() ?? 0);
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task GetTokenUsageSummaryAsync_AccumulatesAndFilters()
    {
        var database = new TestAssetDatabase(DatabasePath);
        await database.EnsureSchemaAsync();
        await SeedAssetAsync(database);
        var store = new AssetDescriptionStore(WriteQueue, database);

        var usage = new AssetDescriptionTokenUsage(100, 10, 110, null, null, null, null, null, null, 0.001);
        await store.SaveAsync(CreateDocument("第一次", DateTimeOffset.UtcNow) with { TokenUsage = usage });
        await store.SaveAsync(CreateDocument("第二次", DateTimeOffset.UtcNow.AddSeconds(1))
            with { TokenUsage = usage with { InputTokens = 200, TotalTokens = 210 } });

        var summary = await store.GetTokenUsageSummaryAsync(assetId: 1);
        Assert.Equal(2, summary.CallCount);
        Assert.Equal(300, summary.TotalInputTokens);
        Assert.Equal(20, summary.TotalOutputTokens);
        Assert.Equal(320, summary.TotalTokens);
        Assert.Equal(0.002, summary.TotalCostCny, 4);
        Assert.Equal(2, summary.RecentEntries.Count);
        Assert.Equal("sample.mp3", summary.RecentEntries[0].AssetName);
        Assert.Equal(200, summary.RecentEntries[0].InputTokens); // 最近一条是第二次调用

        // limit 生效（按时间倒序取最近 N 条）
        var limited = await store.GetTokenUsageSummaryAsync(assetId: 1, limit: 1);
        Assert.Single(limited.RecentEntries);
        Assert.Equal(200, limited.RecentEntries[0].InputTokens);

        // 按库过滤
        var byLibrary = await store.GetTokenUsageSummaryAsync(libraryId: 10);
        Assert.Equal(2, byLibrary.CallCount);

        // 不存在的素材 → 空汇总
        var empty = await store.GetTokenUsageSummaryAsync(assetId: 999);
        Assert.Equal(0, empty.CallCount);
        Assert.Empty(empty.RecentEntries);
    }

    [Fact]
    public async Task GetTokenUsageSummaryAsync_EmptyTable_ReturnsZeroSummary()
    {
        var database = new TestAssetDatabase(DatabasePath);
        await database.EnsureSchemaAsync();
        var store = new AssetDescriptionStore(WriteQueue, database);

        var summary = await store.GetTokenUsageSummaryAsync();
        Assert.Equal(0, summary.CallCount);
        Assert.Equal(0, summary.TotalTokens);
        Assert.Equal(0, summary.TotalCostCny);
        Assert.Empty(summary.RecentEntries);
    }

    [Fact]
    public async Task AppendApiUsageAsync_RecordsOperationModelAndQuery()
    {
        var database = new TestAssetDatabase(DatabasePath);
        await database.EnsureSchemaAsync();
        await SeedAssetAsync(database);
        var store = new AssetDescriptionStore(WriteQueue, database);

        // 检索类调用:asset_id 为 null
        await store.AppendApiUsageAsync(
            operation: "rerank", mode: "live", model: "qwen3-rerank",
            assetId: null, assetName: "(检索)", assetType: "检索", query: "乐队排练",
            inputTokens: 500, outputTokens: 0, totalTokens: 500, estimatedCostCny: 0.0001);

        // 向量化调用:关联素材
        await store.AppendApiUsageAsync(
            operation: "vectorize", mode: "live", model: "text-embedding-v4@1024d",
            assetId: 1, assetName: "sample.mp3", assetType: "音频", query: null,
            inputTokens: 800, outputTokens: 0, totalTokens: 800, estimatedCostCny: 0.00056);

        await using var connection = await database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT asset_id, asset_name, operation, model, query, total_tokens, estimated_cost_cny
            FROM asset_token_usage_log
            ORDER BY id;
            """;
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.True(reader.IsDBNull(0)); // 检索无素材
        Assert.Equal("(检索)", reader.GetString(1));
        Assert.Equal("rerank", reader.GetString(2));
        Assert.Equal("qwen3-rerank", reader.GetString(3));
        Assert.Equal("乐队排练", reader.GetString(4));
        Assert.Equal(500, reader.GetInt32(5));
        Assert.Equal(0.0001, reader.GetDouble(6), 6);

        Assert.True(await reader.ReadAsync());
        Assert.Equal(1, reader.GetInt64(0));
        Assert.Equal("vectorize", reader.GetString(2));
        Assert.Equal("text-embedding-v4@1024d", reader.GetString(3));
        Assert.True(reader.IsDBNull(4));
        Assert.Equal(800, reader.GetInt32(5));

        // 汇总包含两类调用
        var summary = await store.GetTokenUsageSummaryAsync();
        Assert.Equal(2, summary.CallCount);
        Assert.Equal(1300, summary.TotalTokens);
        Assert.Equal(2, summary.RecentEntries.Count);
        Assert.Equal("vectorize", summary.RecentEntries[0].Operation);
        Assert.Equal("rerank", summary.RecentEntries[1].Operation);
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
            INSERT INTO assets (id, asset_uid, library_id)
            VALUES (1, 'asset_test', 10);
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
                    asset_uid TEXT NOT NULL,
                    library_id INTEGER NULL
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
                CREATE TABLE IF NOT EXISTS asset_token_usage_log (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    asset_id INTEGER NULL REFERENCES assets(id) ON DELETE CASCADE,
                    asset_name TEXT NOT NULL,
                    asset_type TEXT NOT NULL,
                    mode TEXT NOT NULL,
                    operation TEXT NOT NULL DEFAULT 'describe',
                    model TEXT NULL,
                    query TEXT NULL,
                    input_tokens INTEGER NOT NULL DEFAULT 0,
                    output_tokens INTEGER NOT NULL DEFAULT 0,
                    total_tokens INTEGER NOT NULL DEFAULT 0,
                    estimated_cost_cny REAL NOT NULL DEFAULT 0,
                    created_at TEXT NOT NULL
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
