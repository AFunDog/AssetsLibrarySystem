using System;
using System.Threading;
using System.Threading.Tasks;
using AssetsLibrarySystem.Application.Infrastructure;
using Microsoft.Data.Sqlite;

namespace AssetsLibrarySystem.Application.Services.Infrastructure;

public sealed class SqliteAssetDatabase : IAssetDatabase
{
    private readonly SemaphoreSlim _schemaLock = new(1, 1);
    private bool _schemaReady;

    public SqliteAssetDatabase()
    {
        DatabasePath = SharedDataPathHelper.GetDataFilePath("asset_descriptions.db");
    }

    public string DatabasePath { get; }

    public async Task EnsureSchemaAsync(CancellationToken ct = default)
    {
        if (_schemaReady)
        {
            return;
        }

        await _schemaLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_schemaReady)
            {
                return;
            }

            await using var connection = await OpenConnectionWithoutSchemaAsync(ct, configureStoragePragmas: true).ConfigureAwait(false);
            await CreateSchemaAsync(connection, ct).ConfigureAwait(false);
            _schemaReady = true;
        }
        finally
        {
            _schemaLock.Release();
        }
    }

    public async Task<SqliteConnection> OpenConnectionAsync(CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct).ConfigureAwait(false);
        return await OpenConnectionWithoutSchemaAsync(ct, configureStoragePragmas: false).ConfigureAwait(false);
    }

    public SqliteConnection OpenConnection()
    {
        EnsureSchemaAsync().GetAwaiter().GetResult();
        var connection = CreateConnection();
        connection.Open();
        ConfigureOpenConnection(connection, configureStoragePragmas: false);
        return connection;
    }

    private async Task<SqliteConnection> OpenConnectionWithoutSchemaAsync(CancellationToken ct, bool configureStoragePragmas)
    {
        var connection = CreateConnection();
        await connection.OpenAsync(ct).ConfigureAwait(false);
        await ConfigureOpenConnectionAsync(connection, configureStoragePragmas, ct).ConfigureAwait(false);
        return connection;
    }

    private SqliteConnection CreateConnection()
    {
        return new SqliteConnection($"Data Source={DatabasePath}");
    }

    private static async Task ConfigureOpenConnectionAsync(SqliteConnection connection, bool configureStoragePragmas, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = configureStoragePragmas
            ? """
            PRAGMA busy_timeout = 5000;
            PRAGMA foreign_keys = ON;
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;
            """
            : """
            PRAGMA busy_timeout = 5000;
            PRAGMA foreign_keys = ON;
            """;
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static void ConfigureOpenConnection(SqliteConnection connection, bool configureStoragePragmas)
    {
        using var command = connection.CreateCommand();
        command.CommandText = configureStoragePragmas
            ? """
            PRAGMA busy_timeout = 5000;
            PRAGMA foreign_keys = ON;
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;
            """
            : """
            PRAGMA busy_timeout = 5000;
            PRAGMA foreign_keys = ON;
            """;
        command.ExecuteNonQuery();
    }

    private static async Task CreateSchemaAsync(SqliteConnection connection, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS assets (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                asset_uid TEXT NOT NULL,
                library_id TEXT NOT NULL,
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

            CREATE INDEX IF NOT EXISTS ix_assets_content_hash
                ON assets(content_hash);

            CREATE INDEX IF NOT EXISTS ix_assets_current_path
                ON assets(current_path);
            CREATE UNIQUE INDEX IF NOT EXISTS ux_assets_asset_uid
                ON assets(asset_uid);

            CREATE TABLE IF NOT EXISTS asset_metadata (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                asset_uid TEXT NOT NULL,
                tags_json TEXT NOT NULL DEFAULT '[]',
                metadata_status TEXT NOT NULL,
                vector_state TEXT NOT NULL DEFAULT 'pending',
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_asset_metadata_asset_uid
                ON asset_metadata(asset_uid);

            CREATE TABLE IF NOT EXISTS asset_descriptions (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                asset_id TEXT NOT NULL,
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
                metadata_status TEXT NOT NULL DEFAULT 'ready'
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_asset_descriptions_asset_id
                ON asset_descriptions(asset_id);

            CREATE TABLE IF NOT EXISTS asset_description_vectors (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                asset_id TEXT NOT NULL,
                angle_type TEXT NOT NULL DEFAULT '全面',
                embedding_model TEXT NOT NULL,
                vector_dim INTEGER NOT NULL,
                vector_blob BLOB NOT NULL,
                vectorized_at TEXT NOT NULL,
                content_hash TEXT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_asset_description_vectors_identity
                ON asset_description_vectors(asset_id, angle_type, embedding_model);
            CREATE INDEX IF NOT EXISTS ix_asset_description_vectors_embedding_model
                ON asset_description_vectors(embedding_model);
            """;
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

        await EnsureColumnAsync(connection, "asset_descriptions", "content_hash", "TEXT NULL", ct).ConfigureAwait(false);
        await EnsureColumnAsync(connection, "asset_descriptions", "metadata_status", "TEXT NOT NULL DEFAULT 'ready'", ct).ConfigureAwait(false);
        await EnsureColumnAsync(connection, "asset_description_vectors", "angle_type", "TEXT NOT NULL DEFAULT '全面'", ct).ConfigureAwait(false);
        await EnsureColumnAsync(connection, "asset_description_vectors", "content_hash", "TEXT NULL", ct).ConfigureAwait(false);
    }

    private static async Task EnsureColumnAsync(
        SqliteConnection connection,
        string tableName,
        string columnName,
        string definition,
        CancellationToken ct)
    {
        if (await ColumnExistsAsync(connection, tableName, columnName, ct).ConfigureAwait(false))
        {
            return;
        }

        await using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {definition};";
        await alter.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task<bool> ColumnExistsAsync(
        SqliteConnection connection,
        string tableName,
        string columnName,
        CancellationToken ct)
    {
        await using var pragma = connection.CreateCommand();
        pragma.CommandText = $"PRAGMA table_info({tableName});";

        await using var reader = await pragma.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
