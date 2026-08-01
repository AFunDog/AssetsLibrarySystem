using System;
using System.Collections;
using System.Diagnostics;
using System.Linq;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using AssetsLibrarySystem.Application.Models;
using AssetsLibrarySystem.Avalonia.Models;
using AssetsLibrarySystem.Avalonia.ViewModels;
using AvaloniaInput = global::Avalonia.Input;

namespace AssetsLibrarySystem.Avalonia.Views.Pages;

public partial class LibraryPage : UserControl
{
    public LibraryPage()
    {
        InitializeComponent();
        // 启用拖拽接收
        AvaloniaInput.DragDrop.SetAllowDrop(ExplorerDropArea, true);
        // 注册拖拽事件
        AddHandler(AvaloniaInput.DragDrop.DragOverEvent, Explorer_DragOver);
        AddHandler(AvaloniaInput.DragDrop.DropEvent, Explorer_Drop);
    }

    // ===== 保留在 View 层的代码（需要系统对话框交互） =====

    private async void AddLibraryFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storageProvider ||
            DataContext is not LibraryPageViewModel viewModel)
        {
            return;
        }

        var isClipLibrary = sender is Button { CommandParameter: "clip" };

        var folders = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = isClipLibrary ? "选择视频剪辑库目录（仅收录视频）" : "选择素材库目录",
            AllowMultiple = false
        });

        var folderPath = folders.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return;
        }

        await viewModel.Workspace.AddLibraryDirectoryAsync(folderPath, isClipLibrary ? LibraryKind.Clip : LibraryKind.Standard);
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

        await viewModel.QueueDescriptionForNodeAsync(node);
    }

    private async void VectorizeDescriptionsForNode_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem ||
            menuItem.CommandParameter is not AssetLibraryTreeNode node ||
            DataContext is not LibraryPageViewModel viewModel)
        {
            return;
        }

        await viewModel.VectorizeDescriptionsForNodeAsync(node);
    }

    private async void DeleteDescriptionForNode_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem ||
            menuItem.CommandParameter is not AssetLibraryTreeNode node ||
            DataContext is not LibraryPageViewModel viewModel)
        {
            return;
        }

        await viewModel.DeleteDescriptionForNodeAsync(node);
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

    private void EditTags_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem ||
            menuItem.CommandParameter is not AssetLibraryTreeNode node ||
            DataContext is not LibraryPageViewModel viewModel)
        {
            return;
        }

        viewModel.Workspace.SelectedAssetTreeNode = node;
    }

    private void RenameNode_Click(object? sender, RoutedEventArgs e)
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

    // ===== 拖拽事件处理器 =====

    private void Explorer_DragOver(object? sender, AvaloniaInput.DragEventArgs e)
    {
        e.DragEffects = AvaloniaInput.DragDropEffects.Copy;
        e.Handled = true;
    }

    private async void Explorer_Drop(object? sender, AvaloniaInput.DragEventArgs e)
    {
        if (DataContext is not LibraryPageViewModel viewModel)
            return;

        // 尝试通过反射获取文件数据（兼容不同 Avalonia 版本 API）
        try
        {
            var dataProp = e.GetType().GetProperty("Data")
                ?? e.GetType().GetProperty("DataObject");

            if (dataProp?.GetValue(e) is not { } dataObj)
                return;

            // 尝试 GetFiles()
            var getFilesMethod = dataObj.GetType().GetMethod("GetFiles");
            var files = getFilesMethod?.Invoke(dataObj, null);

            if (files is IEnumerable enumerable)
            {
                foreach (var item in enumerable)
                {
                    var path = item.GetType()
                        .GetMethod("TryGetLocalPath")
                        ?.Invoke(item, null) as string;

                    if (string.IsNullOrWhiteSpace(path))
                        continue;

                    if (Directory.Exists(path))
                        await viewModel.Workspace.AddLibraryDirectoryAsync(path);
                    else if (File.Exists(path))
                    {
                        var dir = Path.GetDirectoryName(path);
                        if (!string.IsNullOrWhiteSpace(dir))
                            await viewModel.Workspace.AddLibraryDirectoryAsync(dir);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"DragDrop error: {ex.Message}");
        }

        e.Handled = true;
    }
}