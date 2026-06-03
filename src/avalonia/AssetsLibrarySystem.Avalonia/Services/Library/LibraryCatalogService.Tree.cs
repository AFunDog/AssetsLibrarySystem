using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AssetsLibrarySystem.Avalonia.Models;

namespace AssetsLibrarySystem.Avalonia.Services.Library;

public sealed partial class LibraryCatalogService
{
    private void UpdateSelectedAssetDetails(ManagedAssetRecord? value)
    {
        if (value is null)
        {
            SelectedAssetName = "尚未选择素材";
            SelectedAssetLibrary = "请先扫描一个素材库";
            SelectedAssetPath = "当前未加载本地文件路径";
            SelectedAssetType = "未选择";
            SelectedAssetStage = "待选择";
            SelectedAssetAiState = "未排队";
            SelectedAssetDetail = "右侧详情区域会展示当前素材的路径、类型和扫描结果。";
            ResetSelectedAssetDescription();
            return;
        }

        SelectedAssetName = value.Name;
        SelectedAssetLibrary = value.LibraryName;
        SelectedAssetPath = value.LocalPath;
        SelectedAssetType = value.AssetType;
        SelectedAssetStage = value.Stage;
        SelectedAssetAiState = value.AiState;
        SelectedAssetDetail = value.Summary;
        ResetSelectedAssetDescription();
    }

    private IEnumerable<ManagedAssetRecord> EnumerateDescriptionSelectionAssets()
    {
        if (SelectedAsset is not null)
        {
            yield return SelectedAsset;
            yield break;
        }

        if (SelectedAssetTreeNode is null)
        {
            yield break;
        }

        if (SelectedAssetTreeNode.Kind == AssetLibraryTreeNodeKind.Library && SelectedAssetTreeNode.Library is not null)
        {
            foreach (var asset in AllAssets.Where(asset => asset.LibraryName == SelectedAssetTreeNode.Library.Name))
            {
                yield return asset;
            }

            yield break;
        }

        var fullPath = SelectedAssetTreeNode.FullPath;
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            yield break;
        }

        var normalizedPrefix = NormalizePathPrefix(fullPath);
        foreach (var asset in AllAssets.Where(asset => NormalizePathPrefix(asset.LocalPath).StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase)))
        {
            yield return asset;
        }
    }

    private void UpdateDescriptionSelectionSummary()
    {
        var assets = EnumerateDescriptionSelectionAssets().ToList();
        if (assets.Count == 0)
        {
            DescriptionSelectionSummary = "请选择左侧素材库、目录或单个素材，再安排描述任务。";
            return;
        }

        DescriptionSelectionSummary = $"{BuildDescriptionSelectionLabel()} · 共 {assets.Count} 个素材可发送到后端描述。";
    }

    private string BuildDescriptionSelectionLabel()
    {
        if (SelectedAsset is not null)
        {
            return $"当前素材：{SelectedAsset.Name}";
        }

        if (SelectedAssetTreeNode?.Kind == AssetLibraryTreeNodeKind.Library)
        {
            return $"当前素材库：{SelectedAssetTreeNode.DisplayName}";
        }

        if (SelectedAssetTreeNode is not null)
        {
            return $"当前目录：{SelectedAssetTreeNode.DisplayName}";
        }

        return "当前选择";
    }

    private void RebuildAssetTree()
    {
        AssetTreeRoots.Clear();
        foreach (var library in Libraries.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            AssetTreeRoots.Add(BuildLibraryTree(library));
        }
    }

    private AssetLibraryTreeNode BuildLibraryTree(LibraryWorkspace library)
    {
        var libraryAssets = AllAssets
            .Where(asset => asset.LibraryName == library.Name)
            .OrderBy(asset => asset.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var root = new AssetLibraryTreeNode
        {
            DisplayName = library.Name,
            MetaLabel = BuildCountLabel(libraryAssets.Count),
            CategorySummary = BuildCategorySummary(libraryAssets.Select(asset => asset.AssetType)),
            TypeLabel = "素材库",
            StatusLabel = library.SyncMode,
            PathLabel = library.RootPath,
            Summary = library.Summary,
            IconKind = "Folder",
            FullPath = library.RootPath,
            Kind = AssetLibraryTreeNodeKind.Library,
            Library = library
        };

        var directories = new Dictionary<string, AssetLibraryTreeNode>(StringComparer.OrdinalIgnoreCase);
        foreach (var asset in libraryAssets)
        {
            var currentNode = root;
            var relativeSegments = asset.RelativePath.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);

            for (var index = 0; index < relativeSegments.Length - 1; index++)
            {
                var folderKey = string.Join('/', relativeSegments.Take(index + 1));
                if (!directories.TryGetValue(folderKey, out var folderNode))
                {
                    folderNode = new AssetLibraryTreeNode
                    {
                        DisplayName = relativeSegments[index],
                        TypeLabel = "目录",
                        PathLabel = folderKey,
                        Summary = $"目录 · {folderKey}",
                        IconKind = "Folder",
                        FullPath = Path.Combine(library.RootPath, folderKey.Replace('/', Path.DirectorySeparatorChar)),
                        Kind = AssetLibraryTreeNodeKind.Directory,
                        Library = library
                    };

                    directories[folderKey] = folderNode;
                    currentNode.Children.Add(folderNode);
                }

                currentNode = folderNode;
            }

            currentNode.Children.Add(new AssetLibraryTreeNode
            {
                DisplayName = asset.Name,
                MetaLabel = asset.AssetType,
                CategorySummary = asset.Stage,
                TypeLabel = asset.AssetType,
                StatusLabel = asset.Stage,
                PathLabel = asset.RelativePath,
                Summary = asset.Summary,
                IconKind = "File",
                FullPath = asset.LocalPath,
                Kind = AssetLibraryTreeNodeKind.File,
                Library = library,
                Asset = asset
            });
        }

        PopulateDirectoryStatistics(root);
        return root;
    }

    private void PopulateDirectoryStatistics(AssetLibraryTreeNode node)
    {
        foreach (var child in node.Children)
        {
            PopulateDirectoryStatistics(child);
        }

        if (node.Kind == AssetLibraryTreeNodeKind.File)
        {
            return;
        }

        var assetNodes = EnumerateAssetNodes(node).ToList();
        node.MetaLabel = BuildCountLabel(assetNodes.Count);
        node.CategorySummary = BuildCategorySummary(assetNodes.Select(assetNode => assetNode.TypeLabel));
    }

    private IEnumerable<AssetLibraryTreeNode> EnumerateAssetNodes(AssetLibraryTreeNode node)
    {
        foreach (var child in node.Children)
        {
            if (child.Kind == AssetLibraryTreeNodeKind.File)
            {
                yield return child;
                continue;
            }

            foreach (var descendant in EnumerateAssetNodes(child))
            {
                yield return descendant;
            }
        }
    }

    private static string BuildCountLabel(int count)
    {
        return count == 0 ? "空目录" : $"{count} 项";
    }

    private static string BuildCategorySummary(IEnumerable<string> categories)
    {
        var values = categories
            .Where(category => !string.IsNullOrWhiteSpace(category))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(category => category, StringComparer.Ordinal)
            .ToList();

        return values.Count == 0 ? "暂无素材" : string.Join(" / ", values);
    }

    private void RestoreTreeSelection(LibraryWorkspace library)
    {
        if (SelectedAsset is not null)
        {
            SelectedAssetTreeNode = FindAssetTreeNode(SelectedAsset.Id);
            return;
        }

        SelectedAssetTreeNode = FindLibraryTreeNode(library.Id);
    }

    private void UpdateExplorerView(AssetLibraryTreeNode? node)
    {
        var container = GetExplorerContainerNode(node);

        CurrentExplorerItems.Clear();
        if (container is null)
        {
            ExplorerTitle = "素材库";
            ExplorerSummary = "选择一个素材库后，中央区域会显示该库下的目录和文件。";
            ExplorerPath = "未选择";
            CanNavigateUp = false;

            foreach (var libraryRoot in AssetTreeRoots)
            {
                CurrentExplorerItems.Add(libraryRoot);
            }

            return;
        }

        foreach (var item in container.Children)
        {
            CurrentExplorerItems.Add(item);
        }

        ExplorerTitle = container.DisplayName;
        ExplorerSummary = container.Kind == AssetLibraryTreeNodeKind.Library
            ? container.Summary
            : $"{container.MetaLabel} · {container.CategorySummary}";
        ExplorerPath = container.FullPath;
        CanNavigateUp = container.Kind != AssetLibraryTreeNodeKind.Library && FindParentTreeNode(container) is not null;
    }

    private void OpenExplorerItem(AssetLibraryTreeNode? node)
    {
        if (node is null)
        {
            return;
        }

        SelectedAssetTreeNode = node;
    }

    private void NavigateUp()
    {
        var container = GetExplorerContainerNode(SelectedAssetTreeNode);
        if (container is null)
        {
            return;
        }

        if (container.Kind == AssetLibraryTreeNodeKind.Library)
        {
            SelectedAssetTreeNode = null;
            return;
        }

        var parent = FindParentTreeNode(container);
        if (parent is not null)
        {
            SelectedAssetTreeNode = parent;
        }
    }

    private AssetLibraryTreeNode? GetExplorerContainerNode(AssetLibraryTreeNode? node)
    {
        if (node is null)
        {
            return null;
        }

        if (node.Kind != AssetLibraryTreeNodeKind.File)
        {
            return node;
        }

        return FindParentTreeNode(node);
    }

    private AssetLibraryTreeNode? FindParentTreeNode(AssetLibraryTreeNode node)
    {
        if (node.Kind == AssetLibraryTreeNodeKind.Library)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(node.FullPath))
        {
            return null;
        }

        var parentPath = Path.GetDirectoryName(node.FullPath);
        if (string.IsNullOrWhiteSpace(parentPath))
        {
            return null;
        }

        return FindTreeNodeByPath(parentPath);
    }

    private AssetLibraryTreeNode? FindTreeNodeByPath(string path)
    {
        var normalizedPath = NormalizePath(path);
        foreach (var root in AssetTreeRoots)
        {
            var match = FindTreeNodeByPathRecursive(root, normalizedPath);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private AssetLibraryTreeNode? FindTreeNodeByPathRecursive(AssetLibraryTreeNode node, string normalizedPath)
    {
        if (string.Equals(NormalizePath(node.FullPath), normalizedPath, StringComparison.OrdinalIgnoreCase))
        {
            return node;
        }

        foreach (var child in node.Children)
        {
            var match = FindTreeNodeByPathRecursive(child, normalizedPath);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }

    private AssetLibraryTreeNode? FindLibraryTreeNode(string libraryId)
    {
        return AssetTreeRoots.FirstOrDefault(node => node.Library?.Id == libraryId);
    }

    private AssetLibraryTreeNode? FindAssetTreeNode(string assetId)
    {
        foreach (var root in AssetTreeRoots)
        {
            var match = FindAssetTreeNodeRecursive(root, assetId);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private AssetLibraryTreeNode? FindAssetTreeNodeRecursive(AssetLibraryTreeNode node, string assetId)
    {
        if (node.Asset?.Id == assetId)
        {
            return node;
        }

        foreach (var child in node.Children)
        {
            var match = FindAssetTreeNodeRecursive(child, assetId);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private void ApplyTreeSelection(AssetLibraryTreeNode? node)
    {
        if (node is null)
        {
            return;
        }

        if (node.Library is not null && !ReferenceEquals(SelectedLibrary, node.Library))
        {
            SuppressLibrarySelectionLoad = true;
            SelectedLibrary = node.Library;
            SuppressLibrarySelectionLoad = false;
        }

        WorkspaceTitle = node.DisplayName;
        WorkspaceSummary = node.FullPath;
        AssetSummary = node.Summary;
        SelectedAsset = node.Asset;

        if (node.Library is not null &&
            node.Asset is null &&
            !AllAssets.Any(asset => asset.LibraryName == node.Library.Name))
        {
            _ = LoadSelectedLibraryAsync(node.Library);
        }
    }

    private void RebuildMetrics()
    {
        Metrics.Clear();

        var totalAssets = AllAssets.Count;
        var pendingModel = AllAssets.Count(asset => asset.AiState.Contains("待", StringComparison.Ordinal));
        var recognizedTypes = AllAssets
            .Select(asset => asset.AssetType)
            .Distinct(StringComparer.Ordinal)
            .Count();

        Metrics.Add(new DashboardMetric("本地素材库", Libraries.Count.ToString("D2"), "Avalonia 侧维护目录登记"));
        Metrics.Add(new DashboardMetric("已扫描素材", totalAssets.ToString("D2"), "文本 | 图片 | 视频 | 音频"));
        Metrics.Add(new DashboardMetric("待模型处理", pendingModel.ToString("D2"), "仅把提示词和推理请求交给 Python"));
        Metrics.Add(new DashboardMetric("素材类型", recognizedTypes.ToString("D2"), "当前已识别的文件类型数量"));
    }

    private void SetEmptyWorkspaceState()
    {
        WorkspaceTitle = "尚未添加素材库";
        WorkspaceSummary = "请选择一个本地文件夹并登记为素材库目录。";
        AssetSummary = "支持扫描文本、图片、视频和音频文件。";
        SelectedAsset = null;
    }

    private void SyncSelectedAssetFields()
    {
        if (SelectedAsset is null)
        {
            return;
        }

        SelectedAssetStage = SelectedAsset.Stage;
        SelectedAssetAiState = SelectedAsset.AiState;
        RebuildAssetTree();
        SelectedAssetTreeNode = FindAssetTreeNode(SelectedAsset.Id);
    }

    private static string NormalizePathPrefix(string value)
    {
        return value.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
    }
}
