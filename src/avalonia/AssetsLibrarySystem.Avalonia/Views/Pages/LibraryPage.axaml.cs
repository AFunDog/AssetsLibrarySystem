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
            DataContext is not MainWindowViewModel viewModel)
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

    private void SelectLibrary_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        if (button.DataContext is LibraryWorkspace library)
        {
            viewModel.SelectedLibrary = library;
        }
    }

    private void SelectAsset_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        if (button.DataContext is ManagedAssetRecord asset)
        {
            viewModel.SelectedAsset = asset;
        }
    }
}
