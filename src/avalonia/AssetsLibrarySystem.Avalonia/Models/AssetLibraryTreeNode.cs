using System;
using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using AssetsLibrarySystem.Application.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AssetsLibrarySystem.Avalonia.Models;

public enum AssetLibraryTreeNodeKind
{
    Library,
    Directory,
    File
}

public sealed partial class AssetLibraryTreeNode : ObservableObject
{
    public string DisplayName { get; init; } = string.Empty;

    public string MetaLabel
    {
        get;
        set => SetProperty(ref field, value);
    } = string.Empty;

    public string CategorySummary
    {
        get;
        set => SetProperty(ref field, value);
    } = string.Empty;

    public string TypeLabel { get; init; } = string.Empty;
    public string StatusLabel { get; init; } = string.Empty;

    public string DescriptionProgressLabel
    {
        get;
        set => SetProperty(ref field, value);
    } = string.Empty;

    public string SizeLabel { get; init; } = string.Empty;
    public string PathLabel { get; init; } = string.Empty;

    /// <summary>路径与显示名重复（如库根目录文件）时不显示路径行，避免信息冗余</summary>
    public bool ShowPathLabel => !string.IsNullOrWhiteSpace(PathLabel)
        && !string.Equals(PathLabel, DisplayName, StringComparison.OrdinalIgnoreCase);
    public string Summary { get; init; } = string.Empty;
    public string IconKind { get; init; } = "Folder";
    public string FullPath { get; init; } = string.Empty;
    public AssetLibraryTreeNodeKind Kind { get; init; }
    public LibraryWorkspace? Library { get; init; }
    public ManagedAssetRecord? Asset { get; init; }
    public ObservableCollection<AssetLibraryTreeNode> Children { get; } = [];

    /// <summary>状态颜色键，用于绑定到主题资源，如 "StatusDescribedBrush"</summary>
    public string StatusColorKey => (Asset, Kind) switch
    {
        ({ IsDescribed: true, IsVectorized: true }, _) => "StatusVectorizedBrush",
        ({ IsDescribed: true }, _) => "StatusDescribedBrush",
        ({ Stage: "描述中" }, _) => "StatusProcessingBrush",
        ({ Stage: "描述失败" }, _) => "StatusFailedBrush",
        (not null, _) => "StatusPendingBrush",
        (null, AssetLibraryTreeNodeKind.File) => "StatusPendingBrush",
        _ => "StatusPendingBrush"
    };

    /// <summary>是否显示状态指示点</summary>
    public bool ShowStatusDot => Kind == AssetLibraryTreeNodeKind.File && Asset is not null;

    /// <summary>缩略图（图片素材才有值）</summary>
    public Bitmap? Thumbnail
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(HasThumbnail));
            }
        }
    }

    /// <summary>是否有缩略图</summary>
    public bool HasThumbnail => Thumbnail is not null;
}
