using System;
using System.Threading;
using System.Threading.Tasks;
using AssetsLibrarySystem.Application.Infrastructure;
using Microsoft.Data.Sqlite;
using Serilog;

namespace AssetsLibrarySystem.Application.Services.Infrastructure;

public sealed class SqliteAssetDatabase : IAssetDatabase
{
    private readonly SemaphoreSlim _schemaLock = new(1, 1);
    private bool _schemaReady;
    private IDatabaseWriteQueue? WriteQueue { get; }

    public SqliteAssetDatabase(IDatabaseWriteQueue? writeQueue = null)
    {
        WriteQueue = writeQueue;
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
        EnsureSchema();
        var connection = CreateConnection();
        connection.Open();
        ConfigureOpenConnection(connection, configureStoragePragmas: false);
        return connection;
    }

    private void EnsureSchema()
    {
        if (_schemaReady)
        {
            return;
        }

        _schemaLock.Wait();
        try
        {
            if (_schemaReady)
            {
                return;
            }

            using var connection = OpenConnectionWithoutSchema();
            CreateSchema(connection);
            _schemaReady = true;
        }
        finally
        {
            _schemaLock.Release();
        }
    }

    private SqliteConnection OpenConnectionWithoutSchema()
    {
        return CreateConnectionAndOpen(configureStoragePragmas: true);
    }

    private SqliteConnection CreateConnectionAndOpen(bool configureStoragePragmas)
    {
        var connection = CreateConnection();
        connection.Open();
        ConfigureOpenConnection(connection, configureStoragePragmas);
        return connection;
    }

    private static void CreateSchema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS libraries (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL,
                root_path TEXT NOT NULL,
                kind TEXT NOT NULL DEFAULT 'standard',
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_libraries_root_path
                ON libraries(root_path);

            CREATE TABLE IF NOT EXISTS assets (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
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

            CREATE INDEX IF NOT EXISTS ix_assets_content_hash
                ON assets(content_hash);

            CREATE INDEX IF NOT EXISTS ix_assets_current_path
                ON assets(current_path);
            CREATE UNIQUE INDEX IF NOT EXISTS ux_assets_asset_uid
                ON assets(asset_uid);

            CREATE TABLE IF NOT EXISTS asset_metadata (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                asset_id INTEGER NOT NULL REFERENCES assets(id) ON DELETE CASCADE,
                tags_json TEXT NOT NULL DEFAULT '[]',
                metadata_status TEXT NOT NULL,
                vector_state TEXT NOT NULL DEFAULT 'pending',
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_asset_metadata_asset_id
                ON asset_metadata(asset_id);

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
                metadata_status TEXT NOT NULL DEFAULT 'ready'
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_asset_descriptions_asset_id
                ON asset_descriptions(asset_id);

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
            CREATE INDEX IF NOT EXISTS ix_asset_token_usage_log_asset_id
                ON asset_token_usage_log(asset_id);

            CREATE TABLE IF NOT EXISTS asset_description_vectors (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                asset_id INTEGER NOT NULL REFERENCES assets(id) ON DELETE CASCADE,
                angle_type TEXT NOT NULL DEFAULT '整体',
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
        command.ExecuteNonQuery();

        EnsureUsageLogSchema(connection);

        EnsureColumn(connection, "asset_descriptions", "content_hash", "TEXT NULL");
        EnsureColumn(connection, "asset_descriptions", "metadata_status", "TEXT NOT NULL DEFAULT 'ready'");
        EnsureColumn(connection, "asset_description_vectors", "angle_type", "TEXT NOT NULL DEFAULT '整体'");
        EnsureColumn(connection, "asset_description_vectors", "content_hash", "TEXT NULL");
        EnsureColumn(connection, "asset_description_vectors", "source_fingerprint", "TEXT NULL");
        EnsureColumn(connection, "asset_metadata", "subtype", "TEXT NULL");
        EnsureColumn(connection, "libraries", "kind", "TEXT NOT NULL DEFAULT 'standard'");
    }

    private static void EnsureColumn(SqliteConnection connection, string tableName, string columnName, string definition)
    {
        if (ColumnExists(connection, tableName, columnName))
        {
            return;
        }

        using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {definition};";
        alter.ExecuteNonQuery();
    }

    private static bool ColumnExists(SqliteConnection connection, string tableName, string columnName)
    {
        using var pragma = connection.CreateCommand();
        pragma.CommandText = $"PRAGMA table_info({tableName});";
        using var reader = pragma.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// usage 流水表迁移：旧结构（asset_id NOT NULL、无 operation/model/query 列）重建为新结构，
    /// 保留已有描述调用记录。同步版。
    /// </summary>
    private static void EnsureUsageLogSchema(SqliteConnection connection)
    {
        if (!ColumnExists(connection, "asset_token_usage_log", "operation"))
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
            BEGIN;
            CREATE TABLE asset_token_usage_log_new (
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
            INSERT INTO asset_token_usage_log_new (
                id, asset_id, asset_name, asset_type, mode,
                input_tokens, output_tokens, total_tokens, estimated_cost_cny, created_at
            )
            SELECT id, asset_id, asset_name, asset_type, mode,
                   input_tokens, output_tokens, total_tokens, estimated_cost_cny, created_at
            FROM asset_token_usage_log;
            DROP TABLE asset_token_usage_log;
            ALTER TABLE asset_token_usage_log_new RENAME TO asset_token_usage_log;
            CREATE INDEX IF NOT EXISTS ix_asset_token_usage_log_asset_id
                ON asset_token_usage_log(asset_id);
            CREATE INDEX IF NOT EXISTS ix_asset_token_usage_log_operation
                ON asset_token_usage_log(operation);
            COMMIT;
            """;
            try
            {
                command.ExecuteNonQuery();
            }
            catch
            {
                // 迁移失败时回滚，避免旧表被 DROP 后无法恢复（历史数据留在日志表即可）
                using var rollback = connection.CreateCommand();
                rollback.CommandText = "ROLLBACK;";
                try
                {
                    rollback.ExecuteNonQuery();
                }
                catch (Exception rollbackEx)
                {
                    Log.Warning(rollbackEx, "usage 表迁移回滚失败");
                }

                throw;
            }

            return;
        }

        // 新结构已就位时，幂等补建 operation 索引（旧库重建后可能缺失）
        using (var indexCommand = connection.CreateCommand())
        {
            indexCommand.CommandText = """
                CREATE INDEX IF NOT EXISTS ix_asset_token_usage_log_operation
                    ON asset_token_usage_log(operation);
                """;
            indexCommand.ExecuteNonQuery();
        }
    }

    /// <summary>usage 流水表迁移，异步版。</summary>
    private static async Task EnsureUsageLogSchemaAsync(SqliteConnection connection, CancellationToken ct)
    {
        if (!await ColumnExistsAsync(connection, "asset_token_usage_log", "operation", ct).ConfigureAwait(false))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
            BEGIN;
            CREATE TABLE asset_token_usage_log_new (
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
            INSERT INTO asset_token_usage_log_new (
                id, asset_id, asset_name, asset_type, mode,
                input_tokens, output_tokens, total_tokens, estimated_cost_cny, created_at
            )
            SELECT id, asset_id, asset_name, asset_type, mode,
                   input_tokens, output_tokens, total_tokens, estimated_cost_cny, created_at
            FROM asset_token_usage_log;
            DROP TABLE asset_token_usage_log;
            ALTER TABLE asset_token_usage_log_new RENAME TO asset_token_usage_log;
            CREATE INDEX IF NOT EXISTS ix_asset_token_usage_log_asset_id
                ON asset_token_usage_log(asset_id);
            CREATE INDEX IF NOT EXISTS ix_asset_token_usage_log_operation
                ON asset_token_usage_log(operation);
            COMMIT;
            """;
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return;
        }

        // 新结构已就位时，幂等补建 operation 索引（旧库重建后可能缺失）
        await using (var indexCommand = connection.CreateCommand())
        {
            indexCommand.CommandText = """
                CREATE INDEX IF NOT EXISTS ix_asset_token_usage_log_operation
                    ON asset_token_usage_log(operation);
                """;
            await indexCommand.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    public async Task UpdateSubtypeAsync(long assetId, string subtype, CancellationToken ct = default)
    {
        async Task WriteCoreAsync(CancellationToken token)
        {
            await using var connection = await OpenConnectionAsync(token).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE asset_metadata
                SET subtype = $subtype,
                    updated_at = $updated_at
                WHERE asset_id = $assetId;
                """;
            command.Parameters.AddWithValue("$subtype", subtype);
            command.Parameters.AddWithValue("$assetId", assetId);
            command.Parameters.AddWithValue("$updated_at", DateTimeOffset.UtcNow.ToString("O"));
            var affected = await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            if (affected == 0)
            {
                // metadata 行可能尚未创建（仅有 assets）
                await using var insert = connection.CreateCommand();
                insert.CommandText = """
                    INSERT INTO asset_metadata (
                        asset_id, tags_json, metadata_status, vector_state, subtype, created_at, updated_at
                    )
                    VALUES (
                        $assetId, '[]', 'pending', 'pending', $subtype, $updated_at, $updated_at
                    )
                    ON CONFLICT(asset_id) DO UPDATE SET
                        subtype = excluded.subtype,
                        updated_at = excluded.updated_at;
                    """;
                insert.Parameters.AddWithValue("$subtype", subtype);
                insert.Parameters.AddWithValue("$assetId", assetId);
                insert.Parameters.AddWithValue("$updated_at", DateTimeOffset.UtcNow.ToString("O"));
                await insert.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }
        }

        if (WriteQueue is null)
        {
            await WriteCoreAsync(ct).ConfigureAwait(false);
            return;
        }

        await WriteQueue.EnqueueAsync(WriteCoreAsync, ct).ConfigureAwait(false);
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
            CREATE TABLE IF NOT EXISTS libraries (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL,
                root_path TEXT NOT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_libraries_root_path
                ON libraries(root_path);

            CREATE TABLE IF NOT EXISTS assets (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
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

            CREATE INDEX IF NOT EXISTS ix_assets_content_hash
                ON assets(content_hash);

            CREATE INDEX IF NOT EXISTS ix_assets_current_path
                ON assets(current_path);
            CREATE UNIQUE INDEX IF NOT EXISTS ux_assets_asset_uid
                ON assets(asset_uid);

            CREATE TABLE IF NOT EXISTS asset_metadata (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                asset_id INTEGER NOT NULL REFERENCES assets(id) ON DELETE CASCADE,
                tags_json TEXT NOT NULL DEFAULT '[]',
                metadata_status TEXT NOT NULL,
                vector_state TEXT NOT NULL DEFAULT 'pending',
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_asset_metadata_asset_id
                ON asset_metadata(asset_id);

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
                metadata_status TEXT NOT NULL DEFAULT 'ready'
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_asset_descriptions_asset_id
                ON asset_descriptions(asset_id);

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
            CREATE INDEX IF NOT EXISTS ix_asset_token_usage_log_asset_id
                ON asset_token_usage_log(asset_id);

            CREATE TABLE IF NOT EXISTS asset_description_vectors (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                asset_id INTEGER NOT NULL REFERENCES assets(id) ON DELETE CASCADE,
                angle_type TEXT NOT NULL DEFAULT '整体',
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

        await EnsureUsageLogSchemaAsync(connection, ct).ConfigureAwait(false);

        await EnsureColumnAsync(connection, "asset_descriptions", "content_hash", "TEXT NULL", ct).ConfigureAwait(false);
        await EnsureColumnAsync(connection, "asset_descriptions", "metadata_status", "TEXT NOT NULL DEFAULT 'ready'", ct).ConfigureAwait(false);
        await EnsureColumnAsync(connection, "asset_description_vectors", "angle_type", "TEXT NOT NULL DEFAULT '整体'", ct).ConfigureAwait(false);
        await EnsureColumnAsync(connection, "asset_description_vectors", "content_hash", "TEXT NULL", ct).ConfigureAwait(false);
        await EnsureColumnAsync(connection, "asset_description_vectors", "source_fingerprint", "TEXT NULL", ct).ConfigureAwait(false);
        await EnsureColumnAsync(connection, "asset_metadata", "subtype", "TEXT NULL", ct).ConfigureAwait(false);
        await EnsureColumnAsync(connection, "libraries", "kind", "TEXT NOT NULL DEFAULT 'standard'", ct).ConfigureAwait(false);
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
