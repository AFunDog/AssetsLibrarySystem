using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AssetsLibrarySystem.Avalonia.Models;

namespace AssetsLibrarySystem.Avalonia.Services.AssetLibrary;

public sealed class AssetLibraryService : IAssetLibraryService
{
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".json", ".yaml", ".yml", ".csv", ".log", ".xml"
    };

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".avif"
    };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".avi", ".mov", ".webm", ".wmv", ".flv"
    };

    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".wav", ".flac", ".ogg", ".m4a", ".aac", ".wma"
    };

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _storePath;

    public AssetLibraryService()
    {
        // 素材库登记信息只保存在程序工作目录下的 data 目录。
        var dataDirectory = Path.Combine(Directory.GetCurrentDirectory(), "data");
        Directory.CreateDirectory(dataDirectory);
        _storePath = Path.Combine(dataDirectory, "libraries.json");
    }

    public async Task<IReadOnlyList<LibraryWorkspace>> GetLibrariesAsync(CancellationToken ct = default)
    {
        var items = await ReadStoreAsync(ct);
        return items
            .Select(item => new LibraryWorkspace(
                item.Id,
                item.Name,
                item.RootPath,
                "尚未扫描，点击“扫描当前素材库”加载素材文件。",
                "已登记目录",
                0))
            .ToList();
    }

    public async Task<LibraryWorkspace> AddLibraryAsync(string folderPath, CancellationToken ct = default)
    {
        var normalizedPath = Path.GetFullPath(folderPath);
        if (!Directory.Exists(normalizedPath))
        {
            throw new DirectoryNotFoundException($"目录不存在：{normalizedPath}");
        }

        var items = await ReadStoreAsync(ct);
        var existing = items.FirstOrDefault(item =>
            string.Equals(item.RootPath, normalizedPath, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            return new LibraryWorkspace(
                existing.Id,
                existing.Name,
                existing.RootPath,
                "目录已存在，可直接扫描。",
                "已登记目录",
                0);
        }

        var created = new LibraryStoreItem
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = BuildLibraryName(normalizedPath, items),
            RootPath = normalizedPath
        };

        items.Add(created);
        await WriteStoreAsync(items, ct);

        return new LibraryWorkspace(
            created.Id,
            created.Name,
            created.RootPath,
            "目录已登记，等待首次扫描。",
            "已登记目录",
            0);
    }

    public Task<IReadOnlyList<ManagedAssetRecord>> ScanLibraryAsync(LibraryWorkspace library, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        // 只扫描当前支持的文件类型，其他文件保持忽略。
        var files = EnumerateSupportedFiles(library.RootPath)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => BuildAssetRecord(library, path))
            .ToList();

        return Task.FromResult<IReadOnlyList<ManagedAssetRecord>>(files);
    }

    private ManagedAssetRecord BuildAssetRecord(LibraryWorkspace library, string fullPath)
    {
        var relativePath = Path.GetRelativePath(library.RootPath, fullPath);
        var fileInfo = new FileInfo(fullPath);
        var assetType = ClassifyAssetType(fullPath);
        var extension = fileInfo.Extension.TrimStart('.').ToLowerInvariant();
        var summary = $"{assetType}文件 · {FormatFileSize(fileInfo.Length)} · 修改于 {fileInfo.LastWriteTime:yyyy-MM-dd HH:mm}";

        return new ManagedAssetRecord(
            $"{library.Id}:{relativePath.Replace('\\', '/')}",
            fileInfo.Name,
            library.Name,
            assetType,
            relativePath,
            fullPath,
            summary,
            "已扫描",
            "未提交模型",
            new[] { assetType, extension });
    }

    private IEnumerable<string> EnumerateSupportedFiles(string rootPath)
    {
        if (!Directory.Exists(rootPath))
        {
            yield break;
        }

        // 使用显式栈遍历，避免递归过深导致的栈溢出。
        var pending = new Stack<string>();
        pending.Push(rootPath);

        while (pending.Count > 0)
        {
            var current = pending.Pop();

            IEnumerable<string> subDirectories;
            try
            {
                subDirectories = Directory.EnumerateDirectories(current);
            }
            catch
            {
                continue;
            }

            foreach (var subDirectory in subDirectories)
            {
                pending.Push(subDirectory);
            }

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(current);
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                if (TryClassifyAssetType(file, out _))
                {
                    yield return file;
                }
            }
        }
    }

    private static string BuildLibraryName(string normalizedPath, IEnumerable<LibraryStoreItem> existing)
    {
        var baseName = Path.GetFileName(normalizedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = normalizedPath;
        }

        var used = new HashSet<string>(existing.Select(item => item.Name), StringComparer.OrdinalIgnoreCase);
        if (!used.Contains(baseName))
        {
            return baseName;
        }

        var index = 2;
        while (used.Contains($"{baseName} {index}"))
        {
            index++;
        }

        return $"{baseName} {index}";
    }

    private static string ClassifyAssetType(string path)
    {
        return TryClassifyAssetType(path, out var type) ? type : "其他";
    }

    private static bool TryClassifyAssetType(string path, out string type)
    {
        var extension = Path.GetExtension(path);
        // 后续如果扩展素材类型，只需要维护这几组扩展名集合。
        if (TextExtensions.Contains(extension))
        {
            type = "文本";
            return true;
        }

        if (ImageExtensions.Contains(extension))
        {
            type = "图片";
            return true;
        }

        if (VideoExtensions.Contains(extension))
        {
            type = "视频";
            return true;
        }

        if (AudioExtensions.Contains(extension))
        {
            type = "音频";
            return true;
        }

        type = string.Empty;
        return false;
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        if (bytes < 1024 * 1024)
        {
            return $"{bytes / 1024d:F1} KB";
        }

        if (bytes < 1024L * 1024 * 1024)
        {
            return $"{bytes / 1024d / 1024:F1} MB";
        }

        return $"{bytes / 1024d / 1024 / 1024:F1} GB";
    }

    private async Task<List<LibraryStoreItem>> ReadStoreAsync(CancellationToken ct)
    {
        if (!File.Exists(_storePath))
        {
            return [];
        }

        await using var stream = File.OpenRead(_storePath);
        return await JsonSerializer.DeserializeAsync<List<LibraryStoreItem>>(stream, _jsonOptions, ct) ?? [];
    }

    private async Task WriteStoreAsync(List<LibraryStoreItem> items, CancellationToken ct)
    {
        await using var stream = File.Create(_storePath);
        await JsonSerializer.SerializeAsync(stream, items, _jsonOptions, ct);
    }

    private sealed class LibraryStoreItem
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string RootPath { get; set; } = string.Empty;
    }
}
