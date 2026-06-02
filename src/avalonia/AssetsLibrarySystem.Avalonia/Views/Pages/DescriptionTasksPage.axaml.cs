using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using AssetsLibrarySystem.Avalonia.Models;
using AssetsLibrarySystem.Avalonia.ViewModels;

namespace AssetsLibrarySystem.Avalonia.Views.Pages;

public partial class DescriptionTasksPage : UserControl
{
    public DescriptionTasksPage()
    {
        InitializeComponent();
    }

    private async void AddLibraryFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storageProvider ||
            DataContext is not DescriptionTasksPageViewModel viewModel)
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

        await viewModel.AddLibraryDirectoryAsync(folderPath);
    }

    private void RevealInExplorer_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem ||
            menuItem.CommandParameter is not AssetLibraryTreeNode node ||
            DataContext is not DescriptionTasksPageViewModel viewModel)
        {
            return;
        }

        viewModel.RevealInFileExplorer(node);
    }
}
