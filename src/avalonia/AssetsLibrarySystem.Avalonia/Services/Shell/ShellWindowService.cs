using System;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using AssetsLibrarySystem.Avalonia.Views;
using Serilog;

namespace AssetsLibrarySystem.Avalonia.Services.Shell;

public sealed class ShellWindowService : IShellWindowService
{
    private IClassicDesktopStyleApplicationLifetime? Desktop { get; set; }
    private MainWindow? MainWindow { get; set; }
    private QuickSearchWindow? QuickSearchWindow { get; set; }
    private bool IsShuttingDown { get; set; }

    public event Action<bool>? MainWindowVisibilityChanged;
    public event Action<bool>? QuickSearchWindowVisibilityChanged;

    public bool IsMainWindowVisible => MainWindow?.IsVisible == true;
    public bool IsQuickSearchWindowVisible => QuickSearchWindow?.IsVisible == true;

    public void AttachDesktop(IClassicDesktopStyleApplicationLifetime desktop)
    {
        Desktop = desktop;
    }

    public void AttachMainWindow(MainWindow window)
    {
        MainWindow = window;
        window.Closing += MainWindow_Closing;
        MainWindowVisibilityChanged?.Invoke(IsMainWindowVisible);
    }

    public void AttachQuickSearchWindow(QuickSearchWindow window)
    {
        QuickSearchWindow = window;
        window.Closing += QuickSearchWindow_Closing;
        QuickSearchWindowVisibilityChanged?.Invoke(IsQuickSearchWindowVisible);
    }

    public void SetShuttingDown(bool isShuttingDown)
    {
        IsShuttingDown = isShuttingDown;
    }

    public void RequestShutdown()
    {
        IsShuttingDown = true;
        Desktop?.Shutdown();
    }

    public void ShowMainWindow()
    {
        ShowWindow(MainWindow);
        MainWindowVisibilityChanged?.Invoke(IsMainWindowVisible);
    }

    public void ShowQuickSearchWindow()
    {
        ShowWindow(QuickSearchWindow);
        QuickSearchWindow?.FocusSearchBox();
        QuickSearchWindowVisibilityChanged?.Invoke(IsQuickSearchWindowVisible);
    }

    public void ToggleQuickSearchWindow()
    {
        if (QuickSearchWindow is null)
        {
            return;
        }

        if (QuickSearchWindow.IsVisible)
        {
            HideQuickSearchWindow();
            return;
        }

        ShowQuickSearchWindow();
    }

    public void FocusQuickSearchWindow()
    {
        QuickSearchWindow?.FocusSearchBox();
    }

    public void HideMainWindow()
    {
        HideWindow(MainWindow);
        MainWindowVisibilityChanged?.Invoke(IsMainWindowVisible);
    }

    public void HideQuickSearchWindow()
    {
        HideWindow(QuickSearchWindow);
        QuickSearchWindowVisibilityChanged?.Invoke(IsQuickSearchWindowVisible);
    }

    private void MainWindow_Closing(object? sender, WindowClosingEventArgs e)
    {
        if (IsShuttingDown)
        {
            return;
        }

        e.Cancel = true;
        HideMainWindow();
        Log.Debug("主窗口关闭请求被拦截，已转为隐藏。");
    }

    private void QuickSearchWindow_Closing(object? sender, WindowClosingEventArgs e)
    {
        if (IsShuttingDown)
        {
            return;
        }

        e.Cancel = true;
        HideQuickSearchWindow();
        Log.Debug("快速检索窗口关闭请求被拦截，已转为隐藏。");
    }

    private static void ShowWindow(Window? window)
    {
        if (window is null)
        {
            return;
        }

        if (!window.IsVisible)
        {
            window.Show();
        }

        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Activate();
    }

    private static void HideWindow(Window? window)
    {
        window?.Hide();
    }
}
