using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using AssetsLibrarySystem.Avalonia.Models;
using AssetsLibrarySystem.Avalonia.ViewModels;

namespace AssetsLibrarySystem.Avalonia.Views.Pages;

public partial class LibraryPage : UserControl
{
    public LibraryPage()
    {
        InitializeComponent();
    }

    private async void AddLibraryFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storageProvider ||
            DataContext is not LibraryPageViewModel viewModel)
        {
            return;
        }

        // 目录登记直接走系统文件夹选择器。
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

        await viewModel.AddLibraryDirectoryAsync(folderPath);
    }

    private void RevealInExplorer_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem ||
            menuItem.CommandParameter is not AssetLibraryTreeNode node ||
            DataContext is not LibraryPageViewModel viewModel)
        {
            return;
        }

        viewModel.RevealInFileExplorer(node);
    }

    private async void QueueDescriptionForNode_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem ||
            menuItem.CommandParameter is not AssetLibraryTreeNode node ||
            DataContext is not LibraryPageViewModel viewModel)
        {
            return;
        }

        await viewModel.QueueDescriptionForNodeAsync(node);
    }

    private void RevealSearchResult_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button ||
            button.CommandParameter is not AssetSearchDocument result ||
            DataContext is not LibraryPageViewModel viewModel)
        {
            return;
        }

        viewModel.RevealSearchResultInExplorer(result);
    }

    private void SelectLibrary_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button ||
            button.CommandParameter is not LibraryWorkspace library ||
            DataContext is not LibraryPageViewModel viewModel)
        {
            return;
        }

        viewModel.SelectLibrary(library);
    }

    private void OpenExplorerItem_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button ||
            button.CommandParameter is not AssetLibraryTreeNode node ||
            DataContext is not LibraryPageViewModel viewModel)
        {
            return;
        }

        viewModel.SelectedAssetTreeNode = node;
    }
}
