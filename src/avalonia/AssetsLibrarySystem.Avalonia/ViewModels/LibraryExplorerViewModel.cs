using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;
using AssetsLibrarySystem.Application.Models;
using AssetsLibrarySystem.Avalonia.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AssetsLibrarySystem.Avalonia.ViewModels;

/// <summary>
/// 素材库资源管理器 ViewModel，委托给 LibraryWorkspaceViewModel。
/// </summary>
public sealed partial class LibraryExplorerViewModel : ObservableObject
{
    private LibraryWorkspaceViewModel Workspace { get; }

    public LibraryExplorerViewModel(LibraryWorkspaceViewModel workspace)
    {
        Workspace = workspace;
        Workspace.PropertyChanged += (_, e) => OnPropertyChanged(e.PropertyName);

        ScanSelectedLibraryCommand = new AsyncRelayCommand(() => Workspace.ScanSelectedLibraryAsync());
        OpenLibraryCommand = new RelayCommand<LibraryWorkspace?>(Workspace.SelectLibrary);
        OpenExplorerItemCommand = new RelayCommand<AssetLibraryTreeNode?>(node => Workspace.SelectedAssetTreeNode = node);
        NavigateUpCommand = new RelayCommand(() => Workspace.NavigateUpCommand.Execute(null));
    }

    [Obsolete("仅供设计器使用")]
    public LibraryExplorerViewModel()
        : this(new LibraryWorkspaceViewModel())
    {
    }

    // ===== 委托属性 =====
    public string WorkspaceTitle => Workspace.WorkspaceTitle;
    public string WorkspaceSummary => Workspace.WorkspaceSummary;
    public ObservableCollection<LibraryWorkspace> Libraries => Workspace.Libraries;
    public ObservableCollection<AssetLibraryTreeNode> AssetTreeRoots => Workspace.AssetTreeRoots;
    public ObservableCollection<AssetLibraryTreeNode> CurrentExplorerItems => Workspace.CurrentExplorerItems;
    public string ExplorerTitle => Workspace.ExplorerTitle;
    public string ExplorerSummary => Workspace.ExplorerSummary;
    public string ExplorerPath => Workspace.ExplorerPath;
    public bool CanNavigateUp => Workspace.CanNavigateUp;

    public AssetLibraryTreeNode? SelectedAssetTreeNode
    {
        get => Workspace.SelectedAssetTreeNode;
        set => Workspace.SelectedAssetTreeNode = value;
    }

    public LibraryWorkspace? SelectedLibrary => Workspace.SelectedLibrary;

    public IAsyncRelayCommand ScanSelectedLibraryCommand { get; }
    public IRelayCommand<LibraryWorkspace?> OpenLibraryCommand { get; }
    public IRelayCommand<AssetLibraryTreeNode?> OpenExplorerItemCommand { get; }
    public IRelayCommand NavigateUpCommand { get; }

    public Task AddLibraryDirectoryAsync(string folderPath)
        => Workspace.AddLibraryDirectoryAsync(folderPath);
}