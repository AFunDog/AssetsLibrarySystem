using AssetsLibrarySystem.Application.DependencyInjection;
using AssetsLibrarySystem.Application.Infrastructure;
using AssetsLibrarySystem.ConsoleHost;
using AssetsLibrarySystem.ConsoleHost.DependencyInjection;
using Autofac;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace AssetsLibrarySystem.Application.Tests;

/// <summary>
/// Console 命令冒烟测试。
/// 通过 DATA_ROOT 环境变量隔离数据目录（与真实数据完全隔离），
/// 使用与 Program.cs 相同的 Autofac 容器装配。
/// 串行 collection：环境变量为进程级状态，避免并行冲突。
/// </summary>
[Collection("ConsoleCommandRunnerTests")]
public sealed class ConsoleCommandRunnerTests : IDisposable
{
    private const string DataRootVariable = "DATA_ROOT";
    private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), $"als-console-tests-{Guid.NewGuid():N}");
    private readonly string _libraryPath;
    private readonly string? _previousDataRoot;
    private readonly IContainer _container;

    public ConsoleCommandRunnerTests()
    {
        Directory.CreateDirectory(_dataRoot);
        _libraryPath = Path.Combine(_dataRoot, "素材库");
        Directory.CreateDirectory(_libraryPath);
        File.WriteAllText(Path.Combine(_libraryPath, "readme.txt"), "hello");
        File.WriteAllText(Path.Combine(_libraryPath, "clip.mp4"), "fake");

        _previousDataRoot = Environment.GetEnvironmentVariable(DataRootVariable);
        Environment.SetEnvironmentVariable(DataRootVariable, _dataRoot);

        var builder = new ContainerBuilder();
        builder.RegisterInstance(ApplicationConfigurationFactory.CreateConfiguration())
            .As<IConfiguration>()
            .SingleInstance();
        builder.RegisterModule<ApplicationModule>();
        builder.RegisterModule<ConsoleHostModule>();
        _container = builder.Build();
    }

    private async Task<int> RunAsync(params string[] args)
    {
        using var scope = _container.BeginLifetimeScope();
        var runner = scope.Resolve<ConsoleCommandRunner>();
        return await runner.RunAsync(args);
    }

    [Fact]
    public async Task LibrariesAdd_WithClipKind_RegistersClipLibrary()
    {
        var exitCode = await RunAsync("libraries", "add", _libraryPath, "--kind", "clip");

        Assert.Equal(0, exitCode);
        await using (var connection = new SqliteConnection($"Data Source={DatabasePath}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT kind FROM libraries WHERE root_path = $path;";
            command.Parameters.AddWithValue("$path", _libraryPath);
            var kind = (string)(await command.ExecuteScalarAsync())!;
            Assert.Equal("clip", kind);
        }
    }

    [Fact]
    public async Task LibrariesRenameAndRemove_FullLifecycle()
    {
        Assert.Equal(0, await RunAsync("libraries", "add", _libraryPath));
        Assert.Equal(0, await RunAsync("libraries", "rename", _libraryPath, "--name", "新名字"));

        await using (var connection = new SqliteConnection($"Data Source={DatabasePath}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT name FROM libraries WHERE root_path = $path;";
            command.Parameters.AddWithValue("$path", _libraryPath);
            Assert.Equal("新名字", (string)(await command.ExecuteScalarAsync())!);
        }

        Assert.Equal(0, await RunAsync("libraries", "remove", _libraryPath));

        await using (var connection = new SqliteConnection($"Data Source={DatabasePath}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM libraries;";
            Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);
        }
    }

    [Fact]
    public async Task AssetsTagAndSetDescription_ThenClear_FullFlow()
    {
        Assert.Equal(0, await RunAsync("libraries", "add", _libraryPath));
        Assert.Equal(0, await RunAsync("libraries", "scan", _libraryPath));

        // 标签：add -> set（全量替换）-> remove
        Assert.Equal(0, await RunAsync("assets", "tag", "--library", _libraryPath, "--asset", "readme.txt", "--add", "文档,测试"));
        Assert.Equal(0, await RunAsync("assets", "tag", "--library", _libraryPath, "--asset", "readme.txt", "--set", "仅文档"));
        Assert.Equal(0, await RunAsync("assets", "tag", "--library", _libraryPath, "--asset", "readme.txt", "--remove", "仅文档"));

        await using (var connection = new SqliteConnection($"Data Source={DatabasePath}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT tags_json FROM asset_metadata
                WHERE asset_id = (SELECT id FROM assets WHERE asset_name = 'readme.txt' LIMIT 1);
                """;
            var tagsJson = (string?)(await command.ExecuteScalarAsync());
            Assert.NotNull(tagsJson);
            Assert.DoesNotContain("文档", tagsJson);
            Assert.DoesNotContain("测试", tagsJson);
        }

        // 无描述记录时 set-description 应创建「手动」文档
        Assert.Equal(0, await RunAsync("assets", "set-description", "--library", _libraryPath, "--asset", "readme.txt", "--text", "手工写入的描述"));

        await using (var connection = new SqliteConnection($"Data Source={DatabasePath}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT description, mode FROM asset_descriptions
                WHERE asset_id = (SELECT id FROM assets WHERE asset_name = 'readme.txt' LIMIT 1);
                """;
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal("手工写入的描述", reader.GetString(0));
            Assert.Equal("手动", reader.GetString(1));
        }

        // 清除描述
        Assert.Equal(0, await RunAsync("assets", "clear-description", "--library", _libraryPath, "--asset", "readme.txt"));
    }

    [Fact]
    public async Task AssetsSetType_InvalidType_ReturnsError()
    {
        Assert.Equal(0, await RunAsync("libraries", "add", _libraryPath));
        Assert.Equal(0, await RunAsync("libraries", "scan", _libraryPath));

        var exitCode = await RunAsync("assets", "set-type", "--library", _libraryPath, "--asset", "readme.txt", "--type", "图片");

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task AssetsDelete_RemovesAsset()
    {
        Assert.Equal(0, await RunAsync("libraries", "add", _libraryPath));
        Assert.Equal(0, await RunAsync("libraries", "scan", _libraryPath));

        Assert.Equal(0, await RunAsync("assets", "delete", "--library", _libraryPath, "--asset", "readme.txt"));

        await using (var connection = new SqliteConnection($"Data Source={DatabasePath}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM assets WHERE asset_name = 'readme.txt';";
            Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);
        }
    }

    [Fact]
    public async Task MissingArguments_ReturnError()
    {
        Assert.Equal(1, await RunAsync("libraries", "remove"));
        Assert.Equal(1, await RunAsync("assets", "delete"));
        Assert.Equal(1, await RunAsync("assets", "rename", "--library", _libraryPath, "--asset", "readme.txt"));
    }

    [Fact]
    public async Task UnknownCommand_ReturnsError()
    {
        Assert.Equal(1, await RunAsync("frobnicate"));
    }

    private string DatabasePath => Path.Combine(_dataRoot, "asset_descriptions.db");

    public void Dispose()
    {
        _container.Dispose();
        // Microsoft.Data.Sqlite 默认连接池会保持文件句柄，须先清池再删目录
        SqliteConnection.ClearAllPools();
        if (_previousDataRoot is null)
        {
            Environment.SetEnvironmentVariable(DataRootVariable, null);
        }
        else
        {
            Environment.SetEnvironmentVariable(DataRootVariable, _previousDataRoot);
        }

        if (Directory.Exists(_dataRoot))
        {
            Directory.Delete(_dataRoot, recursive: true);
        }
    }
}
