using System.Collections.ObjectModel;

namespace AssetsLibrarySystem.Avalonia.Models;

public enum AssetLibraryTreeNodeKind
{
    Library,
    Directory,
    File
}

public sealed class AssetLibraryTreeNode
{
    public string DisplayName { get; init; } = string.Empty;
    public string MetaLabel { get; set; } = string.Empty;
    public string CategorySummary { get; set; } = string.Empty;
    public string TypeLabel { get; init; } = string.Empty;
    public string StatusLabel { get; init; } = string.Empty;
    public string PathLabel { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string IconKind { get; init; } = "Folder";
    public string FullPath { get; init; } = string.Empty;
    public AssetLibraryTreeNodeKind Kind { get; init; }
    public LibraryWorkspace? Library { get; init; }
    public ManagedAssetRecord? Asset { get; init; }
    public ObservableCollection<AssetLibraryTreeNode> Children { get; } = [];
}
