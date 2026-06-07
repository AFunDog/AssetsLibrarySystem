using System;
using System.Collections.ObjectModel;

namespace AssetsLibrarySystem.Application.Models;

public sealed record DashboardMetric(string Label, string Value, string Hint);

public sealed class LibraryWorkspace : ObservableModel
{
    public LibraryWorkspace()
    {
    }

    public LibraryWorkspace(string id, string name, string rootPath, string summary, string syncMode, int assetCount)
    {
        Id = id;
        Name = name;
        RootPath = rootPath;
        Summary = summary;
        SyncMode = syncMode;
        AssetCount = assetCount;
    }

    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string RootPath { get; init; } = string.Empty;

    public string Summary
    {
        get => field;
        set => SetProperty(ref field, value);
    } = string.Empty;

    public string SyncMode
    {
        get => field;
        set => SetProperty(ref field, value);
    } = string.Empty;

    public int AssetCount
    {
        get => field;
        set => SetProperty(ref field, value);
    }
}

public sealed class ManagedAssetRecord : ObservableModel
{
    public string AssetUid { get; init; } = string.Empty;
    public string Id => AssetUid;
    public string Name { get; init; } = string.Empty;
    public string LibraryName { get; init; } = string.Empty;
    public string AssetType { get; init; } = string.Empty;
    public string RelativePath { get; init; } = string.Empty;
    public string LocalPath { get; init; } = string.Empty;
    public string CurrentPath => LocalPath;
    public string ContentHash { get; init; } = string.Empty;
    public string ObservedHash { get; init; } = string.Empty;
    public string MetadataStatus { get; init; } = string.Empty;
    public long FileSize { get; init; }
    public DateTimeOffset ModifiedTimeUtc { get; init; }
    public bool HasUidSidecar { get; init; }

    public bool IsDescribed
    {
        get => field;
        set => SetProperty(ref field, value);
    }
    public string Summary { get; init; } = string.Empty;
    public ObservableCollection<string> Tags { get; init; } = new();
    public string DescriptionStatusLabel => IsDescribed ? "已描述" : "未描述";
    public string FileSizeLabel => FormatFileSize(FileSize);

    public string Stage
    {
        get => field;
        set => SetProperty(ref field, value);
    } = string.Empty;

    public string AiState
    {
        get => field;
        set => SetProperty(ref field, value);
    } = string.Empty;

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
}

public sealed record AiCapabilityRecord(string Name, string Endpoint, string Summary);
