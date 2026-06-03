using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AssetsLibrarySystem.Avalonia.Models;

public sealed record DashboardMetric(string Label, string Value, string Hint);

public sealed partial class LibraryWorkspace : ObservableObject
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

    [ObservableProperty]
    public partial string Summary { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SyncMode { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int AssetCount { get; set; }
}

public sealed partial class ManagedAssetRecord : ObservableObject
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
    public string Summary { get; init; } = string.Empty;
    public ObservableCollection<string> Tags { get; init; } = new();

    [ObservableProperty]
    public partial string Stage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string AiState { get; set; } = string.Empty;
}

public sealed record AiCapabilityRecord(string Name, string Endpoint, string Summary);
