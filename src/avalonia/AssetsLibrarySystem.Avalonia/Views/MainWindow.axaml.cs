using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using AssetsLibrarySystem.Avalonia.ViewModels;
using CommunityToolkit.Mvvm.Input;

namespace AssetsLibrarySystem.Avalonia.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        // 关闭拦截（隐藏到托盘）由 ShellWindowService.AttachMainWindow 统一处理，
        // 此处不再重复订阅，避免两套 IsShuttingDown 状态失步。
        KeyDown += MainWindow_KeyDown;
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

    private void MainWindow_KeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
            return;

        // Ctrl+F: 打开快速搜索
        if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.Control)
        {
            if (viewModel.ShowQuickSearchCommand is { } cmd)
                cmd.Execute(null);
            e.Handled = true;
            return;
        }

        // F5: 刷新当前素材库
        if (e.Key == Key.F5 && e.KeyModifiers == KeyModifiers.None)
        {
            if (viewModel.RefreshWorkspaceCommand is { } cmd)
                _ = cmd.ExecuteAsync(null);
            e.Handled = true;
            return;
        }
    }
}
