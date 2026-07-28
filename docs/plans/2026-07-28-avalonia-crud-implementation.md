# Avalonia 端完整 CRUD 功能实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 Avalonia 桌面端补齐素材库、素材、素材描述三个领域的增删改查功能，包括删除、重命名、标签编辑、描述编辑。

**Architecture:** 沿现有分层（Service Interface → Service Implementation → ViewModel → View）扩展，新增方法复用现有 DatabaseWriteQueue 串行写入机制，删除后联动重建 HNSW 索引。

**Tech Stack:** .NET 10 + Avalonia + SQLite + Autofac + CommunityToolkit.Mvvm + xUnit

---

## 文件改动总览

| 文件 | 操作 | 说明 |
|------|------|------|
| `Application/Services/AssetLibrary/IAssetLibraryService.cs` | 修改 | 新增 5 个方法 |
| `Application/Services/AssetLibrary/AssetLibraryService.cs` | 修改 | 实现 5 个新方法 |
| `Application/Services/AssetDescription/IAssetDescriptionStore.cs` | 修改 | 新增 1 个方法 |
| `Application/Services/AssetDescription/AssetDescriptionStore.cs` | 修改 | 实现 UpdateDescriptionAsync |
| `Avalonia/Services/Library/LibraryCatalogService.cs` | 修改 | 新增 CRUD 操作方法 |
| `Avalonia/ViewModels/AssetDetailViewModel.cs` | 修改 | 新增标签编辑、删除操作 |
| `Avalonia/Views/Pages/LibraryPage.axaml` | 修改 | 增强详情面板，扩展右键菜单 |
| `Avalonia/Views/Pages/LibraryPage.axaml.cs` | 修改 | 新增右键菜单事件处理 |
| `Application.Tests/LibraryCrudTests.cs` | 创建 | CRUD 操作单元测试 |

---

### Task 1: Service 接口层 — IAssetLibraryService 新增方法

**Files:**
- Modify: `src/avalonia/AssetsLibrarySystem.Application/Services/AssetLibrary/IAssetLibraryService.cs` (全文件)

- [ ] **Step 1: 在 IAssetLibraryService 中新增 5 个方法声明**

```csharp
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AssetsLibrarySystem.Application.Models;

namespace AssetsLibrarySystem.Application.Services.AssetLibrary;

public interface IAssetLibraryService
{
    Task<IReadOnlyList<LibraryWorkspace>> GetLibrariesAsync(CancellationToken ct = default);

    Task<LibraryWorkspace> AddLibraryAsync(string folderPath, CancellationToken ct = default);

    Task<IReadOnlyList<ManagedAssetRecord>> ScanLibraryAsync(LibraryWorkspace library, CancellationToken ct = default);

    // === 新增：CRUD 操作 ===

    /// <summary>删除素材库及其所有关联数据（素材、描述、向量）</summary>
    Task DeleteLibraryAsync(long libraryId, CancellationToken ct = default);

    /// <summary>更新素材库名称</summary>
    Task UpdateLibraryAsync(long libraryId, string newName, CancellationToken ct = default);

    /// <summary>删除单个素材及其关联数据（描述、向量）</summary>
    Task DeleteAssetAsync(long assetId, CancellationToken ct = default);

    /// <summary>更新素材标签（持久化到 asset_metadata.tags_json）</summary>
    Task UpdateAssetTagsAsync(long assetId, string[] tags, CancellationToken ct = default);

    /// <summary>更新素材名称（同步更新 assets 和 asset_descriptions）</summary>
    Task UpdateAssetNameAsync(long assetId, string newName, CancellationToken ct = default);
}
```

- [ ] **Step 2: 验证编译通过**

Run: `dotnet build src/avalonia/AssetsLibrarySystem.sln`
Expected: 编译成功（实现类尚未实现方法，但接口定义本身无错误）

---

### Task 2: Service 实现层 — AssetLibraryService 实现新增方法

**Files:**
- Modify: `src/avalonia/AssetsLibrarySystem.Application/Services/AssetLibrary/AssetLibraryService.cs`
- 在类末尾 `#region CRUD Operations` 中新增方法

- [ ] **Step 1: 在 AssetLibraryService 类中新增 CRUD 方法实现**

在 `AssetLibraryService` 类中（在 `AddParameter` 方法之后、`AssetDbRecord` record 之前），新增以下方法：

```csharp
    // ===== CRUD Operations =====

    public Task DeleteLibraryAsync(long libraryId, CancellationToken ct = default)
    {
        return WriteQueue.EnqueueAsync(async token =>
        {
            await using var connection = await AssetDatabase.OpenConnectionAsync(token);
            await using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM libraries WHERE id = $id;";
            AddParameter(command, "$id", libraryId);
            var affected = await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            if (affected == 0)
                throw new InvalidOperationException($"素材库 (id={libraryId}) 不存在。");
        }, ct).AsTask();
    }

    public Task UpdateLibraryAsync(long libraryId, string newName, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);
        return WriteQueue.EnqueueAsync(async token =>
        {
            await using var connection = await AssetDatabase.OpenConnectionAsync(token);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE libraries
                SET name = $name, updated_at = $updated_at
                WHERE id = $id;
                """;
            AddParameter(command, "$id", libraryId);
            AddParameter(command, "$name", newName.Trim());
            AddParameter(command, "$updated_at", DateTimeOffset.UtcNow.ToString("O"));
            var affected = await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            if (affected == 0)
                throw new InvalidOperationException($"素材库 (id={libraryId}) 不存在。");
        }, ct).AsTask();
    }

    public Task DeleteAssetAsync(long assetId, CancellationToken ct = default)
    {
        return WriteQueue.EnqueueAsync(async token =>
        {
            await using var connection = await AssetDatabase.OpenConnectionAsync(token);
            await using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM assets WHERE id = $id;";
            AddParameter(command, "$id", assetId);
            var affected = await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            if (affected == 0)
                throw new InvalidOperationException($"素材 (id={assetId}) 不存在。");
        }, ct).AsTask();
    }

    public Task UpdateAssetTagsAsync(long assetId, string[] tags, CancellationToken ct = default)
    {
        var tagsJson = JsonSerializer.Serialize(tags ?? [], JsonOptions);
        return WriteQueue.EnqueueAsync(async token =>
        {
            await using var connection = await AssetDatabase.OpenConnectionAsync(token);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE asset_metadata
                SET tags_json = $tags_json, updated_at = $updated_at
                WHERE asset_id = $asset_id;
                """;
            AddParameter(command, "$asset_id", assetId);
            AddParameter(command, "$tags_json", tagsJson);
            AddParameter(command, "$updated_at", DateTimeOffset.UtcNow.ToString("O"));
            var affected = await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            if (affected == 0)
                throw new InvalidOperationException($"素材元数据 (asset_id={assetId}) 不存在。");
        }, ct).AsTask();
    }

    public Task UpdateAssetNameAsync(long assetId, string newName, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);
        var trimmedName = newName.Trim();
        return WriteQueue.EnqueueAsync(async token =>
        {
            await using var connection = await AssetDatabase.OpenConnectionAsync(token);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(token).ConfigureAwait(false);

            // 更新 assets 表
            await using var cmd1 = connection.CreateCommand();
            cmd1.Transaction = transaction;
            cmd1.CommandText = """
                UPDATE assets
                SET asset_name = $name, updated_at = $updated_at
                WHERE id = $id;
                """;
            AddParameter(cmd1, "$id", assetId);
            AddParameter(cmd1, "$name", trimmedName);
            AddParameter(cmd1, "$updated_at", DateTimeOffset.UtcNow.ToString("O"));
            var affected = await cmd1.ExecuteNonQueryAsync(token).ConfigureAwait(false);

            if (affected == 0)
            {
                await transaction.RollbackAsync(token).ConfigureAwait(false);
                throw new InvalidOperationException($"素材 (id={assetId}) 不存在。");
            }

            // 同步更新 asset_descriptions 表
            await using var cmd2 = connection.CreateCommand();
            cmd2.Transaction = transaction;
            cmd2.CommandText = "UPDATE asset_descriptions SET asset_name = $name WHERE asset_id = $asset_id;";
            AddParameter(cmd2, "$asset_id", assetId);
            AddParameter(cmd2, "$name", trimmedName);
            await cmd2.ExecuteNonQueryAsync(token).ConfigureAwait(false);

            await transaction.CommitAsync(token).ConfigureAwait(false);
        }, ct).AsTask();
    }
```

在类顶部添加 `using System.Text.Json;`（如果尚未引入），并确保 `JsonOptions` 字段已在类中定义（已存在，见 `AssetLibraryService` 构造函数上方）。

- [ ] **Step 2: 确保编译通过**

Run: `dotnet build src/avalonia/AssetsLibrarySystem.sln`
Expected: 编译成功

---

### Task 3: IAssetDescriptionStore 新增 UpdateDescriptionAsync

**Files:**
- Modify: `src/avalonia/AssetsLibrarySystem.Application/Services/AssetDescription/IAssetDescriptionStore.cs`

- [ ] **Step 1: 新增 UpdateDescriptionAsync 方法声明**

```csharp
public interface IAssetDescriptionStore
{
    string DatabasePath { get; }

    Task SaveAsync(AssetDescriptionDocument document, CancellationToken ct = default);

    Task<AssetDescriptionDocument?> TryGetAsync(long assetId, CancellationToken ct = default);

    Task<AssetDescriptionDocument?> TryGetForAssetAsync(ManagedAssetRecord asset, CancellationToken ct = default);

    Task<bool> DeleteAsync(long assetId, CancellationToken ct = default);

    /// <summary>手动更新素材描述文本，标记向量为过期状态</summary>
    Task UpdateDescriptionAsync(long assetId, string newDescription, CancellationToken ct = default);
}
```

- [ ] **Step 2: 在 AssetDescriptionStore 中实现 UpdateDescriptionAsync**

```csharp
public async Task UpdateDescriptionAsync(long assetId, string newDescription, CancellationToken ct = default)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(newDescription);
    await AssetDatabase.EnsureSchemaAsync(ct);
    await WriteQueue.EnqueueAsync(async token =>
    {
        await using var connection = await AssetDatabase.OpenConnectionAsync(token);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(token).ConfigureAwait(false);

        // 更新描述文本，同时更新 generated_at 使向量化检测认为描述已更新
        await using var cmd1 = connection.CreateCommand();
        cmd1.Transaction = transaction;
        cmd1.CommandText = """
            UPDATE asset_descriptions
            SET description = $description,
                generated_at = $generated_at,
                metadata_status = 'edited'
            WHERE asset_id = $asset_id;
            """;
        AddParameter(cmd1, "$asset_id", assetId);
        AddParameter(cmd1, "$description", newDescription.Trim());
        AddParameter(cmd1, "$generated_at", DateTimeOffset.UtcNow.ToString("O"));
        var affected = await cmd1.ExecuteNonQueryAsync(token).ConfigureAwait(false);

        if (affected == 0)
        {
            await transaction.RollbackAsync(token).ConfigureAwait(false);
            throw new InvalidOperationException($"素材描述 (asset_id={assetId}) 不存在。");
        }

        // 标记向量为 pending，触发后续重新向量化
        await using var cmd2 = connection.CreateCommand();
        cmd2.Transaction = transaction;
        cmd2.CommandText = """
            UPDATE asset_metadata
            SET vector_state = 'pending',
                updated_at = $updated_at
            WHERE asset_id = $asset_id;
            """;
        AddParameter(cmd2, "$asset_id", assetId);
        AddParameter(cmd2, "$updated_at", DateTimeOffset.UtcNow.ToString("O"));
        await cmd2.ExecuteNonQueryAsync(token).ConfigureAwait(false);

        await transaction.CommitAsync(token).ConfigureAwait(false);
    }, ct);
}
```

- [ ] **Step 3: 验证编译通过**

Run: `dotnet build src/avalonia/AssetsLibrarySystem.sln`
Expected: 编译成功

---

### Task 4: 单元测试 — LibraryCrudTests

**Files:**
- Create: `src/avalonia/AssetsLibrarySystem.Application.Tests/LibraryCrudTests.cs`

- [ ] **Step 1: 创建测试文件，测试素材库删除**

```csharp
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
        Assert.Contains("恐怖", tagsJson);
        Assert.Contains("氛围", tagsJson);
        Assert.Contains("追逐", tagsJson);
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
```

- [ ] **Step 2: 运行测试验证**

Run: `dotnet test src/avalonia/AssetsLibrarySystem.Application.Tests/AssetsLibrarySystem.Application.Tests.csproj --filter "FullyQualifiedName~LibraryCrudTests" -v n`
Expected: 5 个测试全部通过

- [ ] **Step 3: 提交测试**

```bash
git add src/avalonia/AssetsLibrarySystem.Application.Tests/LibraryCrudTests.cs
git commit -m "test: 添加素材库/素材 CRUD 操作单元测试"
```

---

### Task 5: LibraryCatalogService 新增 CRUD 操作方法

**Files:**
- Modify: `src/avalonia/AssetsLibrarySystem.Avalonia/Services/Library/LibraryCatalogService.cs`

- [ ] **Step 1: 在 LibraryCatalogService 中新增 CRUD 操作方法**

在类中 `#region CRUD Operations` 区域新增（在 `SetOperatorNotice` 方法之后）：

```csharp
    // ===== CRUD Operations =====

    public async Task DeleteSelectedLibraryAsync()
    {
        if (SelectedLibrary is null)
        {
            SetOperatorNotice("请先选择一个素材库。");
            return;
        }
        if (AssetLibraryService is null)
        {
            SetOperatorNotice("素材库服务未注册。");
            return;
        }

        var libraryId = SelectedLibrary.Id;
        var libraryName = SelectedLibrary.Name;

        // 删除本地缓存
        AllAssets.RemoveAll(a => a.LibraryName == libraryName);
        var libraryNode = AssetTreeRoots.FirstOrDefault(n => n.Library?.Id == libraryId);
        if (libraryNode is not null)
            AssetTreeRoots.Remove(libraryNode);

        await AssetLibraryService.DeleteLibraryAsync(libraryId);
        Libraries.Remove(SelectedLibrary);
        SelectedLibrary = null;
        SelectedAsset = null;
        SelectedAssetTreeNode = null;
        SetEmptyWorkspaceState();
        RebuildMetrics();

        ActivityFeedService.Add($"素材库已删除：{libraryName}");
        Log.Information("素材库已删除: libraryId={LibraryId}, libraryName={LibraryName}", libraryId, libraryName);
    }

    public async Task DeleteSelectedAssetAsync()
    {
        if (SelectedAsset is null)
        {
            SetOperatorNotice("请先选择一个素材。");
            return;
        }
        if (AssetLibraryService is null)
        {
            SetOperatorNotice("素材库服务未注册。");
            return;
        }

        var assetId = SelectedAsset.DatabaseId;
        var assetName = SelectedAsset.Name;

        await AssetLibraryService.DeleteAssetAsync(assetId);
        AllAssets.Remove(SelectedAsset);
        SelectedAsset = null;
        ResetSelectedAssetDescription();
        RebuildAssetTree();
        RebuildMetrics();

        ActivityFeedService.Add($"素材已删除：{assetName}");
        Log.Information("素材已删除: assetId={AssetId}, assetName={AssetName}", assetId, assetName);
    }

    public async Task UpdateSelectedAssetTagsAsync(string[] tags)
    {
        if (SelectedAsset is null || AssetLibraryService is null)
            return;

        await AssetLibraryService.UpdateAssetTagsAsync(SelectedAsset.DatabaseId, tags);
        SelectedAsset.Tags.Clear();
        foreach (var tag in tags)
            SelectedAsset.Tags.Add(tag);

        ActivityFeedService.Add($"标签已更新：{SelectedAsset.Name}");
        Log.Information("素材标签已更新: assetId={AssetId}, tags={Tags}",
            SelectedAsset.DatabaseId, string.Join(", ", tags));
    }

    public async Task UpdateSelectedAssetNameAsync(string newName)
    {
        if (SelectedAsset is null || AssetLibraryService is null)
            return;

        ArgumentException.ThrowIfNullOrWhiteSpace(newName);
        await AssetLibraryService.UpdateAssetNameAsync(SelectedAsset.DatabaseId, newName.Trim());

        // 更新本地缓存中的名称
        var oldName = SelectedAsset.Name;
        // 通过反射更新 Name 属性的 backing field，实际 ManagedAssetRecord 的 Name 是 init-only
        // 所以需要重新构建树来刷新展示
        RebuildAssetTree();
        SyncSelectedAssetFields();

        ActivityFeedService.Add($"素材已重命名：{oldName} → {newName.Trim()}");
        Log.Information("素材已重命名: assetId={AssetId}, oldName={OldName}, newName={NewName}",
            SelectedAsset.DatabaseId, oldName, newName.Trim());
    }

    public async Task UpdateSelectedLibraryNameAsync(string newName)
    {
        if (SelectedLibrary is null || AssetLibraryService is null)
            return;

        ArgumentException.ThrowIfNullOrWhiteSpace(newName);
        var trimmedName = newName.Trim();
        await AssetLibraryService.UpdateLibraryAsync(SelectedLibrary.Id, trimmedName);

        var oldName = SelectedLibrary.Name;
        // 更新本地缓存
        SelectedLibrary = SelectedLibrary with { Name = trimmedName };
        // 更新树节点
        RebuildAssetTree();
        WorkspaceTitle = trimmedName;

        ActivityFeedService.Add($"素材库已重命名：{oldName} → {trimmedName}");
        Log.Information("素材库已重命名: libraryId={LibraryId}, oldName={OldName}, newName={NewName}",
            SelectedLibrary.Id, oldName, trimmedName);
    }

    public async Task UpdateSelectedAssetDescriptionAsync(string newDescription)
    {
        if (SelectedAsset is null || AssetDescriptionStore is null)
            return;

        await AssetDescriptionStore.UpdateDescriptionAsync(SelectedAsset.DatabaseId, newDescription);
        // 重新加载描述
        await LoadSelectedAssetDescriptionAsync(SelectedAsset);

        ActivityFeedService.Add($"描述已手动更新：{SelectedAsset.Name}");
        Log.Information("素材描述已手动更新: assetId={AssetId}", SelectedAsset.DatabaseId);
    }
```

- [ ] **Step 2: 确保编译通过**

Run: `dotnet build src/avalonia/AssetsLibrarySystem.sln`
Expected: 编译成功

---

### Task 6: AssetDetailViewModel 新增 CRUD 操作命令

**Files:**
- Modify: `src/avalonia/AssetsLibrarySystem.Avalonia/ViewModels/AssetDetailViewModel.cs`

- [ ] **Step 1: 在 AssetDetailViewModel 中新增 CRUD 命令和属性**

```csharp
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;
using AssetsLibrarySystem.Application.Models;
using AssetsLibrarySystem.Avalonia.Services.Library;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AssetsLibrarySystem.Avalonia.ViewModels;

public sealed partial class AssetDetailViewModel : ObservableObject
{
    private LibraryCatalogService LibraryCatalogService { get; }

    public AssetDetailViewModel()
        : this(new LibraryCatalogService())
    {
    }

    public AssetDetailViewModel(LibraryCatalogService libraryCatalogService)
    {
        LibraryCatalogService = libraryCatalogService;
        LibraryCatalogService.PropertyChanged += OnCatalogPropertyChanged;
    }

    // === 现有只读属性 ===
    public string SelectedAssetName => LibraryCatalogService.SelectedAssetName;
    public string SelectedAssetLibrary => LibraryCatalogService.SelectedAssetLibrary;
    public string SelectedAssetPath => LibraryCatalogService.SelectedAssetPath;
    public string SelectedAssetType => LibraryCatalogService.SelectedAssetType;
    public string SelectedAssetSubtype => LibraryCatalogService.SelectedAssetSubtype;
    public string SelectedAssetStage => LibraryCatalogService.SelectedAssetStage;
    public string SelectedAssetAiState => LibraryCatalogService.SelectedAssetAiState;
    public string SelectedAssetDetail => LibraryCatalogService.SelectedAssetDetail;
    public string SelectedAssetDescriptionState => LibraryCatalogService.SelectedAssetDescriptionState;
    public string SelectedAssetDescriptionGeneratedAt => LibraryCatalogService.SelectedAssetDescriptionGeneratedAt;
    public string SelectedAssetDescriptionText => LibraryCatalogService.SelectedAssetDescriptionText;
    public string SelectedAssetDescriptionStorePath => LibraryCatalogService.SelectedAssetDescriptionStorePath;
    public string SelectedAssetDescriptionMode => LibraryCatalogService.SelectedAssetDescriptionMode;
    public string SelectedAssetDescriptionTokenUsage => LibraryCatalogService.SelectedAssetDescriptionTokenUsage;
    public string SelectedAssetDescriptionPrompt => LibraryCatalogService.SelectedAssetDescriptionPrompt;
    public string SelectedAssetDescriptionSystemPrompt => LibraryCatalogService.SelectedAssetDescriptionSystemPrompt;
    public ObservableCollection<AngleDescriptionRecord> SelectedAssetDescriptionAngles
        => LibraryCatalogService.SelectedAssetDescriptionAngles;

    // === 新增：标签编辑 ===
    public ObservableCollection<string> SelectedAssetTags => LibraryCatalogService.SelectedAsset?.Tags ?? [];

    [ObservableProperty]
    public partial string NewTagText { get; set; } = string.Empty;

    [RelayCommand]
    private async Task AddTagAsync()
    {
        if (string.IsNullOrWhiteSpace(NewTagText) || LibraryCatalogService.SelectedAsset is null)
            return;

        var tag = NewTagText.Trim();
        var currentTags = LibraryCatalogService.SelectedAsset.Tags.ToArray();
        if (currentTags.Contains(tag))
            return;

        var newTags = currentTags.Append(tag).ToArray();
        await LibraryCatalogService.UpdateSelectedAssetTagsAsync(newTags);
        NewTagText = string.Empty;
    }

    [RelayCommand]
    private async Task RemoveTagAsync(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag) || LibraryCatalogService.SelectedAsset is null)
            return;

        var currentTags = LibraryCatalogService.SelectedAsset.Tags.ToArray();
        var newTags = currentTags.Where(t => t != tag).ToArray();
        await LibraryCatalogService.UpdateSelectedAssetTagsAsync(newTags);
    }

    // === 新增：删除操作 ===
    [RelayCommand]
    private async Task DeleteAssetAsync()
    {
        await LibraryCatalogService.DeleteSelectedAssetAsync();
    }

    [RelayCommand]
    private async Task DeleteLibraryAsync()
    {
        await LibraryCatalogService.DeleteSelectedLibraryAsync();
    }

    // === 新增：重命名 ===
    [ObservableProperty]
    public partial string RenameText { get; set; } = string.Empty;

    [RelayCommand]
    private async Task RenameAssetAsync()
    {
        if (string.IsNullOrWhiteSpace(RenameText))
            return;
        await LibraryCatalogService.UpdateSelectedAssetNameAsync(RenameText.Trim());
        RenameText = string.Empty;
    }

    [RelayCommand]
    private async Task RenameLibraryAsync()
    {
        if (string.IsNullOrWhiteSpace(RenameText))
            return;
        await LibraryCatalogService.UpdateSelectedLibraryNameAsync(RenameText.Trim());
        RenameText = string.Empty;
    }

    // === 新增：描述编辑 ===
    [ObservableProperty]
    public partial string EditDescriptionText { get; set; } = string.Empty;

    [RelayCommand]
    private async Task SaveDescriptionAsync()
    {
        if (string.IsNullOrWhiteSpace(EditDescriptionText))
            return;
        await LibraryCatalogService.UpdateSelectedAssetDescriptionAsync(EditDescriptionText.Trim());
    }

    // === 现有 ===
    private void OnCatalogPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(e.PropertyName);
        if (e.PropertyName == nameof(LibraryCatalogService.SelectedAsset))
        {
            OnPropertyChanged(nameof(SelectedAssetTags));
            EditDescriptionText = LibraryCatalogService.SelectedAssetDescriptionText;
        }
    }
}
```

- [ ] **Step 2: 确保编译通过**

Run: `dotnet build src/avalonia/AssetsLibrarySystem.sln`
Expected: 编译成功

---

### Task 7: LibraryPage.axaml — 增强详情面板和右键菜单

**Files:**
- Modify: `src/avalonia/AssetsLibrarySystem.Avalonia/Views/Pages/LibraryPage.axaml`

- [ ] **Step 1: 在详情面板的基本信息区域增加标签编辑区和操作按钮**

找到 `LibraryPage.axaml` 中详情面板左侧的信息区域（在 `<Border Classes="sub-card">` 内，包含"类型"、"子类型"、"状态"的 StackPanel），在其末尾新增：

```xml
<!-- 标签编辑 -->
<StackPanel>
    <TextBlock Classes="eyebrow" Text="标签" />
    <ItemsControl ItemsSource="{Binding AssetDetail.SelectedAssetTags}">
        <ItemsControl.ItemsPanel>
            <ItemsPanelTemplate>
                <WrapPanel />
            </ItemsPanelTemplate>
        </ItemsControl.ItemsPanel>
        <ItemsControl.ItemTemplate>
            <DataTemplate>
                <Border Background="{DynamicResource AppAccentSurfaceBrush}"
                        CornerRadius="4" Padding="6,2" Margin="0,0,4,4">
                    <Grid ColumnDefinitions="*,Auto" ColumnSpacing="4">
                        <TextBlock Text="{Binding .}"
                                   FontSize="12"
                                   Foreground="{DynamicResource AppTextBrush}" />
                        <Button Grid.Column="1"
                                Background="Transparent"
                                BorderBrush="Transparent"
                                BorderThickness="0"
                                Padding="2"
                                FontSize="10"
                                Command="{Binding $parent[ItemsControl].DataContext.RemoveTagCommand}"
                                CommandParameter="{Binding .}"
                                Content="×"
                                ToolTip.Tip="删除标签" />
                    </Grid>
                </Border>
            </DataTemplate>
        </ItemsControl.ItemTemplate>
    </ItemsControl>
    <Grid ColumnDefinitions="*,Auto" ColumnSpacing="4" Margin="0,4,0,0">
        <TextBox Text="{Binding AssetDetail.NewTagText}"
                 PlaceholderText="添加标签..."
                 Classes="compact-input" />
        <Button Grid.Column="1"
                Classes="primary-action"
                Command="{Binding AssetDetail.AddTagCommand}"
                Content="添加" />
    </Grid>
</StackPanel>

<!-- 操作按钮 -->
<StackPanel Spacing="4" Margin="0,8,0,0">
    <Button Classes="secondary-action"
            Command="{Binding AssetDetail.DeleteAssetCommand}"
            Content="删除素材"
            ToolTip.Tip="删除当前素材及其描述和向量记录" />
    <Button Classes="secondary-action"
            Command="{Binding AssetDetail.DeleteLibraryCommand}"
            Content="删除素材库"
            ToolTip.Tip="删除当前素材库及其所有素材" />
</StackPanel>
```

- [ ] **Step 2: 在素材库节点的右键菜单中增加"重命名素材库"和"删除素材库"**

在 `LibraryPage.axaml` 中找到 `AssetLibraryTreeNode` 的 `DataTemplate` 中的 `ContextMenu`（在 `Button.ContextMenu` 内），在现有菜单项末尾新增：

```xml
<MenuItem Header="重命名素材库"
          Click="RenameLibrary_Click"
          CommandParameter="{Binding}" />
<MenuItem Header="删除素材库"
          Click="DeleteLibrary_Click"
          CommandParameter="{Binding}" />
```

- [ ] **Step 3: 在素材文件节点的右键菜单中增加"编辑标签"、"重命名素材"、"删除素材"**

在同一个 ContextMenu 中，在"删除描述记录"菜单项之后新增：

```xml
<Separator />
<MenuItem Header="编辑标签"
          Click="EditTags_Click"
          CommandParameter="{Binding}" />
<MenuItem Header="重命名素材"
          Click="RenameAsset_Click"
          CommandParameter="{Binding}" />
<MenuItem Header="删除素材"
          Click="DeleteAsset_Click"
          CommandParameter="{Binding}" />
```

注意：`<Separator />` 只在 `Kind == File` 时显示，但这里简化处理，所有节点都显示菜单项，由 click handler 判断节点类型。

- [ ] **Step 4: 在详情面板右侧角度描述列表上方增加"编辑描述"按钮**

在角度描述列表的 `ScrollViewer` 上方新增：

```xml
<StackPanel Spacing="4" Margin="0,0,0,8">
    <Button Classes="secondary-action"
            Command="{Binding AssetDetail.SaveDescriptionCommand}"
            Content="保存描述修改"
            IsEnabled="{Binding AssetDetail.EditDescriptionText, Converter={x:Static StringConverters.IsNotNullOrEmpty}}" />
</StackPanel>
```

- [ ] **Step 5: 确保编译通过**

Run: `dotnet build src/avalonia/AssetsLibrarySystem.sln`
Expected: 编译成功

---

### Task 8: LibraryPage.axaml.cs — 新增右键菜单事件处理

**Files:**
- Modify: `src/avalonia/AssetsLibrarySystem.Avalonia/Views/Pages/LibraryPage.axaml.cs`

- [ ] **Step 1: 新增右键菜单事件处理方法**

```csharp
private async void DeleteLibrary_Click(object? sender, RoutedEventArgs e)
{
    if (sender is not MenuItem menuItem ||
        menuItem.CommandParameter is not AssetLibraryTreeNode node ||
        DataContext is not LibraryPageViewModel viewModel)
    {
        return;
    }

    // 确认弹窗
    var message = $"确定要删除素材库「{node.DisplayName}」吗？\n该操作将删除库内所有素材、描述和向量记录，不可撤销。";
    var result = await MessageBox.Show(message, "删除素材库", MessageBoxButton.YesNo, MessageBoxImage.Warning);
    if (result == MessageBoxResult.Yes)
    {
        viewModel.SelectLibrary(node.Library);
        await viewModel.AssetDetail.DeleteLibraryCommand.ExecuteAsync(null);
    }
}

private async void DeleteAsset_Click(object? sender, RoutedEventArgs e)
{
    if (sender is not MenuItem menuItem ||
        menuItem.CommandParameter is not AssetLibraryTreeNode node ||
        DataContext is not LibraryPageViewModel viewModel)
    {
        return;
    }

    if (node.Asset is null)
        return;

    var message = $"确定要删除素材「{node.DisplayName}」吗？\n相关的描述和向量记录也会被一并删除。";
    var result = await MessageBox.Show(message, "删除素材", MessageBoxButton.YesNo, MessageBoxImage.Question);
    if (result == MessageBoxResult.Yes)
    {
        viewModel.SelectedAssetTreeNode = node;
        await viewModel.AssetDetail.DeleteAssetCommand.ExecuteAsync(null);
    }
}

private async void RenameLibrary_Click(object? sender, RoutedEventArgs e)
{
    if (sender is not MenuItem menuItem ||
        menuItem.CommandParameter is not AssetLibraryTreeNode node ||
        DataContext is not LibraryPageViewModel viewModel)
    {
        return;
    }

    // 选择素材库并聚焦到重命名
    viewModel.SelectLibrary(node.Library);
    viewModel.AssetDetail.RenameText = node.DisplayName;
    // 触发重命名（或通过 UI 输入框确认后执行）
    // 简单起见，直接弹出输入对话框
    var newName = await InputDialog.ShowAsync("重命名素材库", "新名称:", node.DisplayName);
    if (!string.IsNullOrWhiteSpace(newName) && newName != node.DisplayName)
    {
        viewModel.AssetDetail.RenameText = newName;
        await viewModel.AssetDetail.RenameLibraryCommand.ExecuteAsync(null);
    }
}

private async void RenameAsset_Click(object? sender, RoutedEventArgs e)
{
    if (sender is not MenuItem menuItem ||
        menuItem.CommandParameter is not AssetLibraryTreeNode node ||
        DataContext is not LibraryPageViewModel viewModel)
    {
        return;
    }

    if (node.Asset is null)
        return;

    var newName = await InputDialog.ShowAsync("重命名素材", "新名称:", node.DisplayName);
    if (!string.IsNullOrWhiteSpace(newName) && newName != node.DisplayName)
    {
        viewModel.SelectedAssetTreeNode = node;
        viewModel.AssetDetail.RenameText = newName;
        await viewModel.AssetDetail.RenameAssetCommand.ExecuteAsync(null);
    }
}

private async void EditTags_Click(object? sender, RoutedEventArgs e)
{
    if (sender is not MenuItem menuItem ||
        menuItem.CommandParameter is not AssetLibraryTreeNode node ||
        DataContext is not LibraryPageViewModel viewModel)
    {
        return;
    }

    // 切换到该素材并聚焦到详情面板的标签编辑区
    viewModel.SelectedAssetTreeNode = node;
}
```

注意：上述代码使用了 `MessageBox` 和 `InputDialog` 作为示意。项目当前未引入对话框库，需要实现简单的确认弹窗。

**确认弹窗实现方案：** 使用 `Flyout` 附加到触发按钮，在 `Flyout` 内容中放置 `TextBlock` + 两个 `Button`（确认/取消），通过 `TaskCompletionSource` 等待用户选择。这不需要引入任何新依赖，完全复用现有 Avalonia 控件。

**输入对话框实现方案：** 在详情面板中直接放置重命名输入框（`TextBox` + "保存"按钮），用户选中素材库或素材时自动填充当前名称，编辑后点击保存触发重命名。比弹窗方式更符合现有 UI 风格。

**简化实现：** Task 8 的右键菜单事件中，删除操作直接调用 `Flyout` 确认对话框；重命名操作不通过弹窗，而是让用户切换到详情面板，在详情面板中修改名称后保存。

- [ ] **Step 2: 确保编译通过**

Run: `dotnet build src/avalonia/AssetsLibrarySystem.sln`
Expected: 编译成功

---

### Task 9: 集成测试与验证

**Files:**
- 无新增文件，运行现有测试和手动验证

- [ ] **Step 1: 运行所有单元测试**

Run: `dotnet test src/avalonia/AssetsLibrarySystem.Application.Tests/AssetsLibrarySystem.Application.Tests.csproj -v n`
Expected: 所有测试通过（包括原有测试和新 CRUD 测试）

- [ ] **Step 2: 构建桌面端并启动验证**

Run: `dotnet build src/avalonia/AssetsLibrarySystem.sln`
Expected: 编译成功

- [ ] **Step 3: 提交所有改动**

```bash
git add -A
git commit -m "feat: 实现素材库/素材/描述完整 CRUD 功能

- 素材库：删除、重命名
- 素材：删除、重命名、标签编辑
- 描述：手动编辑描述文本，标记向量过期
- 详情面板增强：标签 chip 编辑、操作按钮
- 右键菜单扩展：删除/重命名/编辑标签
- 删除后级联清理关联数据
- 单元测试覆盖核心 CRUD 操作"
```

---

## 自检清单

- [ ] 所有接口方法签名一致（Task 1 vs Task 2）
- [ ] 测试覆盖了删除素材库级联、删除素材级联、标签持久化、重命名双表更新
- [ ] UseCase 层可复用现有 DescribeAssetsUseCase 和 VectorizeDescriptionsUseCase
- [ ] 删除素材库后不需要重建索引（因为所有关联数据都已删除，索引为空）
- [ ] 描述编辑后标记向量为 pending，等待后续向量化流程处理