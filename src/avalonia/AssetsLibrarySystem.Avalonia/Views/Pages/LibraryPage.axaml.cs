using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using AssetsLibrarySystem.Application.Models;
using AssetsLibrarySystem.Avalonia.Models;
using AssetsLibrarySystem.Avalonia.ViewModels;

namespace AssetsLibrarySystem.Avalonia.Views.Pages;

public partial class LibraryPage : UserControl
{
    public LibraryPage()
    {
        InitializeComponent();
    }

    // ===== 保留在 View 层的代码（需要系统对话框交互） =====

    private async void AddLibraryFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storageProvider ||
            DataContext is not LibraryPageViewModel viewModel)
        {
            return;
        }

        var folders = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择素材库目录",
            AllowMultiple = false
        });

        var folderPath = folders.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return;
        }

        await viewModel.Workspace.AddLibraryDirectoryAsync(folderPath);
    }

    // ===== 右键菜单事件处理器（委托到 ViewModel Command） =====

    private void RevealInExplorer_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem ||
            menuItem.CommandParameter is not AssetLibraryTreeNode node ||
            DataContext is not LibraryPageViewModel viewModel)
        {
            return;
        }

        if (viewModel.RevealInExplorerCommand.CanExecute(node))
            viewModel.RevealInExplorerCommand.Execute(node);
    }

    private async void QueueDescriptionForNode_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem ||
            menuItem.CommandParameter is not AssetLibraryTreeNode node ||
            DataContext is not LibraryPageViewModel viewModel)
        {
            return;
        }

        viewModel.Workspace.SelectedAssetTreeNode = node;
    }

    private async void VectorizeDescriptionsForNode_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem ||
            menuItem.CommandParameter is not AssetLibraryTreeNode node ||
            DataContext is not LibraryPageViewModel viewModel)
        {
            return;
        }

        viewModel.Workspace.SelectedAssetTreeNode = node;
    }

    private async void DeleteDescriptionForNode_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem ||
            menuItem.CommandParameter is not AssetLibraryTreeNode node ||
            DataContext is not LibraryPageViewModel viewModel)
        {
            return;
        }

        viewModel.Workspace.SelectedAssetTreeNode = node;
    }

    private void RevealSearchResult_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button ||
            button.CommandParameter is not AssetSearchDocument result ||
            DataContext is not LibraryPageViewModel viewModel)
        {
            return;
        }

        viewModel.SearchPanel.RevealSearchResultInExplorer(result);
    }

    private void SelectLibrary_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button ||
            button.CommandParameter is not LibraryWorkspace library ||
            DataContext is not LibraryPageViewModel viewModel)
        {
            return;
        }

        if (viewModel.SelectLibraryNodeCommand.CanExecute(library))
            viewModel.SelectLibraryNodeCommand.Execute(library);
    }

    private void OpenExplorerItem_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button ||
            button.CommandParameter is not AssetLibraryTreeNode node ||
            DataContext is not LibraryPageViewModel viewModel)
        {
            return;
        }

        viewModel.Workspace.SelectedAssetTreeNode = node;
    }

    private async void EditTags_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem ||
            menuItem.CommandParameter is not AssetLibraryTreeNode node ||
            DataContext is not LibraryPageViewModel viewModel)
        {
            return;
        }

        viewModel.Workspace.SelectedAssetTreeNode = node;
    }

    private async void RenameNode_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem ||
            menuItem.CommandParameter is not AssetLibraryTreeNode node ||
            DataContext is not LibraryPageViewModel viewModel)
        {
            return;
        }

        viewModel.Workspace.SelectedAssetTreeNode = node;
        viewModel.AssetDetail.RenameText = node.DisplayName;
    }

    private async void DeleteNode_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem ||
            menuItem.CommandParameter is not AssetLibraryTreeNode node ||
            DataContext is not LibraryPageViewModel viewModel)
        {
            return;
        }

        viewModel.Workspace.SelectedAssetTreeNode = node;
        if (node.Kind == AssetLibraryTreeNodeKind.File && node.Asset is not null)
        {
            if (viewModel.AssetDetail.DeleteAssetCommand.CanExecute(null))
                await viewModel.AssetDetail.DeleteAssetCommand.ExecuteAsync(null);
        }
        else if (node.Kind == AssetLibraryTreeNodeKind.Library && node.Library is not null)
        {
            viewModel.Workspace.SelectLibrary(node.Library);
            if (viewModel.AssetDetail.DeleteLibraryCommand.CanExecute(null))
                await viewModel.AssetDetail.DeleteLibraryCommand.ExecuteAsync(null);
        }
    }
}