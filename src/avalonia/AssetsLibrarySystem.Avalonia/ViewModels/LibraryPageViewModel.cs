using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;
using AssetsLibrarySystem.Application.Models;
using AssetsLibrarySystem.Avalonia.Models;
using AssetsLibrarySystem.Avalonia.Services.Activity;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Input;

namespace AssetsLibrarySystem.Avalonia.ViewModels;

/// <summary>
/// 素材库页面 ViewModel，组合子 ViewModel 并暴露给 View。
/// 不再有属性转发，AXAML 直接绑定到子 ViewModel 的属性。
/// </summary>
public sealed partial class LibraryPageViewModel : ObservableObject
{
    // ===== 子 ViewModel =====
    public LibraryWorkspaceViewModel Workspace { get; }
    public AssetDetailViewModel AssetDetail { get; }
    public AssetSearchPanelViewModel SearchPanel { get; }
    public BackendStatusViewModel BackendStatus { get; }
    public ObservableCollection<string> ActivityFeed { get; }

    public LibraryPageViewModel(
        LibraryWorkspaceViewModel workspace,
        AssetDetailViewModel assetDetail,
        AssetSearchPanelViewModel searchPanel,
        BackendStatusViewModel backendStatus,
        ActivityFeedService activityFeedService)
    {
        Workspace = workspace;
        AssetDetail = assetDetail;
        SearchPanel = searchPanel;
        BackendStatus = backendStatus;
        ActivityFeed = activityFeedService.Entries;

        // 子 ViewModel 属性变更冒泡到本层
        Workspace.PropertyChanged += BubblePropertyChanged;
        AssetDetail.PropertyChanged += BubblePropertyChanged;
        SearchPanel.PropertyChanged += BubblePropertyChanged;
        BackendStatus.PropertyChanged += BubblePropertyChanged;
    }

    [Obsolete("仅供设计器使用")]
    public LibraryPageViewModel()
        : this(
            new LibraryWorkspaceViewModel(),
            new AssetDetailViewModel(),
            new AssetSearchPanelViewModel(),
            new BackendStatusViewModel(),
            new ActivityFeedService())
    {
    }

    // ===== 转发方法（非属性，从 code-behind 调用） =====
    public Task AddLibraryDirectoryAsync(string folderPath)
        => Workspace.AddLibraryDirectoryAsync(folderPath);

    public void RevealInFileExplorer(AssetLibraryTreeNode? node)
    {
        if (node is null || string.IsNullOrWhiteSpace(node.FullPath))
            return;
        var path = System.IO.Path.GetFullPath(node.FullPath);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "explorer.exe",
            UseShellExecute = true,
            Arguments = node.Kind == AssetLibraryTreeNodeKind.File
                ? $"/select,\"{path}\""
                : $"\"{path}\""
        });
    }

    public void RevealSearchResultInExplorer(AssetSearchDocument? result)
        => SearchPanel.RevealSearchResultInExplorer(result);

    public void SelectLibrary(LibraryWorkspace? library)
        => Workspace.SelectLibrary(library);

    public Task QueueDescriptionForNodeAsync(AssetLibraryTreeNode? node)
        => Task.CompletedTask; // 由子 ViewModel 处理

    public Task DeleteDescriptionForNodeAsync(AssetLibraryTreeNode? node)
        => Task.CompletedTask;

    // ===== Command 包装，供 AXAML 绑定 =====

    [RelayCommand]
    private void QueueDescriptionsForSelection()
    {
        // 将当前范围的所有素材排入描述队列
        var assets = Workspace.GetDescriptionSelectionAssets();
        if (assets.Count == 0)
            Workspace.SetOperatorNotice("当前范围内没有可描述的素材。");
    }

    [RelayCommand]
    private void QueueSelectedDescription()
    {
        if (Workspace.SelectedAsset is null)
            Workspace.SetOperatorNotice("请先选择一个素材。");
    }

    [RelayCommand]
    private void VectorizeDescriptions()
    {
        var assets = Workspace.GetAllLibraryAssets();
        if (assets.Count == 0)
            Workspace.SetOperatorNotice("当前没有可向量化的素材。");
    }

    [RelayCommand]
    private void RevealInExplorer(AssetLibraryTreeNode? node)
    {
        if (node is null || string.IsNullOrWhiteSpace(node.FullPath))
            return;
        var path = System.IO.Path.GetFullPath(node.FullPath);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "explorer.exe",
            UseShellExecute = true,
            Arguments = node.Kind == AssetLibraryTreeNodeKind.File
                ? $"/select,\"{path}\""
                : $"\"{path}\""
        });
    }

    [RelayCommand]
    private void SelectLibraryNode(LibraryWorkspace? library)
    {
        if (library is not null)
            Workspace.SelectLibrary(library);
    }

    private void BubblePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(e.PropertyName);
    }
}