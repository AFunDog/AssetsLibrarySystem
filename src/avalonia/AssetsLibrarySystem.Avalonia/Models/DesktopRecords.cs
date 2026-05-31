using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AssetsLibrarySystem.Avalonia.Models;

public sealed class DashboardMetric
{
    public DashboardMetric(string label, string value, string hint)
    {
        Label = label;
        Value = value;
        Hint = hint;
    }

    public string Label { get; }
    public string Value { get; }
    public string Hint { get; }
}

public sealed partial class LibraryWorkspace : ObservableObject
{
    public LibraryWorkspace(string id, string name, string rootPath, string summary, string syncMode, int assetCount)
    {
        Id = id;
        Name = name;
        RootPath = rootPath;
        this.summary = summary;
        this.syncMode = syncMode;
        this.assetCount = assetCount;
    }

    public string Id { get; }
    public string Name { get; }
    public string RootPath { get; }

    [ObservableProperty]
    private string summary;

    [ObservableProperty]
    private string syncMode;

    [ObservableProperty]
    private int assetCount;
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
        this.stage = stage;
        this.aiState = aiState;
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
    private string stage;

    [ObservableProperty]
    private string aiState;
}

public sealed class AiCapabilityRecord
{
    public AiCapabilityRecord(string name, string endpoint, string summary)
    {
        Name = name;
        Endpoint = endpoint;
        Summary = summary;
    }

    public string Name { get; }
    public string Endpoint { get; }
    public string Summary { get; }
}
