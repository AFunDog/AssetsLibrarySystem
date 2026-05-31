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
    public required string DisplayName { get; init; }
    public string MetaLabel { get; set; } = string.Empty;
    public string CategorySummary { get; set; } = string.Empty;
    public required string TypeLabel { get; init; }
    public required string StatusLabel { get; init; }
    public required string PathLabel { get; init; }
    public required string Summary { get; init; }
    public required string FullPath { get; init; }
    public required AssetLibraryTreeNodeKind Kind { get; init; }
    public LibraryWorkspace? Library { get; init; }
    public ManagedAssetRecord? Asset { get; init; }
    public ObservableCollection<AssetLibraryTreeNode> Children { get; } = [];
}
