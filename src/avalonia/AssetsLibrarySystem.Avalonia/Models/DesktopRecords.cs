using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AssetsLibrarySystem.Avalonia.Models;

public sealed record DashboardMetric(string Label, string Value, string Hint);

public sealed partial class LibraryWorkspace : ObservableObject
{
    public LibraryWorkspace(string id, string name, string rootPath, string summary, string syncMode, int assetCount)
    {
        Id = id;
        Name = name;
        RootPath = rootPath;
        Summary = summary;
        SyncMode = syncMode;
        AssetCount = assetCount;
    }

    public string Id { get; }
    public string Name { get; }
    public string RootPath { get; }

    [ObservableProperty]
    public partial string Summary { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SyncMode { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int AssetCount { get; set; }
}

public sealed partial class ManagedAssetRecord : ObservableObject
{
    public ManagedAssetRecord(
        string id,
        string name,
        string libraryName,
        string assetType,
        string relativePath,
        string localPath,
        string summary,
        string stage,
        string aiState,
        IEnumerable<string> tags)
    {
        Id = id;
        Name = name;
        LibraryName = libraryName;
        AssetType = assetType;
        RelativePath = relativePath;
        LocalPath = localPath;
        Summary = summary;
        Stage = stage;
        AiState = aiState;
        Tags = new ObservableCollection<string>(tags);
    }

    public string Id { get; }
    public string Name { get; }
    public string LibraryName { get; }
    public string AssetType { get; }
    public string RelativePath { get; }
    public string LocalPath { get; }
    public string Summary { get; }
    public ObservableCollection<string> Tags { get; }

    [ObservableProperty]
    public partial string Stage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string AiState { get; set; } = string.Empty;
}

public sealed record AiCapabilityRecord(string Name, string Endpoint, string Summary);
