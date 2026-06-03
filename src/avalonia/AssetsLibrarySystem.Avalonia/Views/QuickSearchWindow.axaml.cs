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

        Hide();
    }

    private void CloseWindow_Click(object? sender, RoutedEventArgs e)
    {
        Hide();
    }

    private void SearchTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            if (e.Key == Key.Escape)
            {
                Hide();
                e.Handled = true;
            }

            return;
        }

        if (DataContext is QuickSearchViewModel viewModel && viewModel.ExecuteSearchCommand is IAsyncRelayCommand command)
        {
            _ = command.ExecuteAsync(null);
            e.Handled = true;
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
