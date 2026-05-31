using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using AssetsLibrarySystem.Avalonia.ViewModels;

namespace AssetsLibrarySystem.Avalonia.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void MinimizeWindow_Click(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void ToggleMaximizeWindow_Click(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void CloseWindow_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void AddLibraryFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (StorageProvider is null || DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        // 直接使用系统文件夹选择器，让目录登记走本地文件系统而不是手工输入。
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
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

        // Button 直接承载当前库项，点击后把它切换成当前工作库。
        if (button.DataContext is AssetsLibrarySystem.Avalonia.Models.LibraryWorkspace library)
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

        // 右侧素材卡同理，点击后只更新当前选中素材，不引入额外的列表选择态。
        if (button.DataContext is AssetsLibrarySystem.Avalonia.Models.ManagedAssetRecord asset)
        {
            viewModel.SelectedAsset = asset;
        }
    }
}
