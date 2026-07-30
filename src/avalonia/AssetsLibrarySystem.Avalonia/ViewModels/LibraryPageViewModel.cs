using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;
using AssetsLibrarySystem.Application.Models;
using AssetsLibrarySystem.Avalonia.Models;
using AssetsLibrarySystem.Avalonia.Services.Activity;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AssetsLibrarySystem.Avalonia.ViewModels;

/// <summary>
/// 素材库页面 ViewModel，组合子 ViewModel 并暴露给 View。
/// 描述/向量化命令转发到对应面板，右键菜单与工具栏共用同一路径。
/// </summary>
public sealed partial class LibraryPageViewModel : ObservableObject
{
    public LibraryWorkspaceViewModel Workspace { get; }
    public AssetDetailViewModel AssetDetail { get; }
    public AssetSearchPanelViewModel SearchPanel { get; }
    public AssetDescriptionPanelViewModel DescriptionPanel { get; }
    public AssetVectorizationPanelViewModel VectorizationPanel { get; }
    public BackendStatusViewModel BackendStatus { get; }
    public ObservableCollection<ActivityFeedEntry> ActivityFeed { get; }

    public LibraryPageViewModel(
        LibraryWorkspaceViewModel workspace,
        AssetDetailViewModel assetDetail,
        AssetSearchPanelViewModel searchPanel,
        AssetDescriptionPanelViewModel descriptionPanel,
        AssetVectorizationPanelViewModel vectorizationPanel,
        BackendStatusViewModel backendStatus,
        ActivityFeedService activityFeedService)
    {
        Workspace = workspace;
        AssetDetail = assetDetail;
        SearchPanel = searchPanel;
        DescriptionPanel = descriptionPanel;
        VectorizationPanel = vectorizationPanel;
        BackendStatus = backendStatus;
        ActivityFeed = activityFeedService.Entries;

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
            new AssetDescriptionPanelViewModel(),
            new AssetVectorizationPanelViewModel(),
            new BackendStatusViewModel(),
            new ActivityFeedService())
    {
    }

    public Task AddLibraryDirectoryAsync(string folderPath)
        => Workspace.AddLibraryDirectoryAsync(folderPath);

    public void RevealInFileExplorer(AssetLibraryTreeNode? node)
        => RevealInExplorer(node);

    public void RevealSearchResultInExplorer(AssetSearchDocument? result)
        => SearchPanel.RevealSearchResultInExplorer(result);

    public void SelectLibrary(LibraryWorkspace? library)
        => Workspace.SelectLibrary(library);

    public Task QueueDescriptionForNodeAsync(AssetLibraryTreeNode? node)
        => DescriptionPanel.QueueDescriptionForNodeAsync(node);

    public Task DeleteDescriptionForNodeAsync(AssetLibraryTreeNode? node)
        => DescriptionPanel.DeleteDescriptionForNodeAsync(node);

    public Task VectorizeDescriptionsForNodeAsync(AssetLibraryTreeNode? node)
        => VectorizationPanel.VectorizeDescriptionsForNodeAsync(node);

    [RelayCommand]
    private Task QueueDescriptionsForSelectionAsync()
        => DescriptionPanel.QueueDescriptionsForSelectionCommand.ExecuteAsync(null);

    [RelayCommand]
    private Task QueueSelectedDescriptionAsync()
        => DescriptionPanel.QueueSelectedDescriptionCommand.ExecuteAsync(null);

    [RelayCommand]
    private Task VectorizeDescriptionsAsync()
        => VectorizationPanel.VectorizeDescriptionsCommand.ExecuteAsync(null);

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
