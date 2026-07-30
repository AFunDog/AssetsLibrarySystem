using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using AssetsLibrarySystem.Avalonia;
using AssetsLibrarySystem.Application.Models;
using AssetsLibrarySystem.Avalonia.ViewModels;
using CommunityToolkit.Mvvm.Input;

namespace AssetsLibrarySystem.Avalonia.Views;

public partial class QuickSearchWindow : Window
{
    public QuickSearchWindow()
    {
        InitializeComponent();
        Opened += (_, _) => FocusSearchBox();
        Deactivated += QuickSearchWindow_Deactivated;
        Closing += QuickSearchWindow_Closing;
    }

    public void FocusSearchBox()
    {
        SearchTextBox.Focus();
        SearchTextBox.SelectAll();
    }

    private void QuickSearchWindow_Closing(object? sender, WindowClosingEventArgs e)
    {
        if (global::Avalonia.Application.Current is App app && app.ShellViewModel?.IsShuttingDown == false)
        {
            e.Cancel = true;
            Hide();
        }
    }

    private void QuickSearchWindow_Deactivated(object? sender, EventArgs e)
    {
        if (!IsVisible)
        {
            return;
        }

        // Hide();
    }

    private void CloseWindow_Click(object? sender, RoutedEventArgs e)
    {
        Hide();
    }

    private void SearchTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not QuickSearchViewModel viewModel)
            return;

        if (e.Key == Key.Enter)
        {
            if (viewModel.IsHistoryDropdownVisible)
            {
                // 回车时如果有历史建议，关闭下拉并直接搜索当前文本
                viewModel.IsHistoryDropdownVisible = false;
            }

            if (viewModel.ExecuteSearchCommand is IAsyncRelayCommand command)
            {
                _ = command.ExecuteAsync(null);
                e.Handled = true;
            }
            return;
        }

        if (e.Key == Key.Escape)
        {
            if (viewModel.IsHistoryDropdownVisible)
            {
                // 如果下拉可见，先关闭下拉
                viewModel.IsHistoryDropdownVisible = false;
                e.Handled = true;
                return;
            }

            // 搜索框为空时 Esc 隐藏窗口
            if (string.IsNullOrWhiteSpace(SearchTextBox.Text))
            {
                Hide();
                e.Handled = true;
                return;
            }

            // 搜索框有内容时 Esc 清空
            SearchTextBox.Text = string.Empty;
            e.Handled = true;
            return;
        }

        // 下箭头展开历史下拉
        if (e.Key == Key.Down && !viewModel.IsHistoryDropdownVisible && viewModel.HistorySuggestions.Count > 0)
        {
            viewModel.IsHistoryDropdownVisible = true;
            e.Handled = true;
        }
    }

    private void SearchTextBox_TextChanged(object? sender, global::Avalonia.Controls.TextChangedEventArgs e)
    {
        if (DataContext is QuickSearchViewModel viewModel)
        {
            viewModel.UpdateHistorySuggestions(SearchTextBox.Text);
        }
    }

    private void RevealSearchResult_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button ||
            button.CommandParameter is not AssetSearchDocument result ||
            DataContext is not QuickSearchViewModel viewModel)
        {
            return;
        }

        viewModel.RevealSearchResultInExplorerCommand.Execute(result);
    }
}
