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
        Log.Debug("已绑定桌面生命周期。");
    }

    public void AttachMainWindow(MainWindow window)
    {
        MainWindow = window;
        window.Closing += MainWindow_Closing;
        Log.Debug("已绑定主窗口实例。");
        MainWindowVisibilityChanged?.Invoke(IsMainWindowVisible);
    }

    public void AttachQuickSearchWindow(QuickSearchWindow window)
    {
        QuickSearchWindow = window;
        window.Closing += QuickSearchWindow_Closing;
        Log.Debug("已绑定快速检索窗口实例。");
        QuickSearchWindowVisibilityChanged?.Invoke(IsQuickSearchWindowVisible);
    }

    public void SetShuttingDown(bool isShuttingDown)
    {
        IsShuttingDown = isShuttingDown;
        Log.Debug("设置壳层关闭状态: isShuttingDown={IsShuttingDown}", isShuttingDown);
    }

    public void RequestShutdown()
    {
        IsShuttingDown = true;
        Log.Information("请求桌面生命周期退出。");
        Desktop?.Shutdown();
    }

    public void ShowMainWindow()
    {
        Log.Debug("请求显示主窗口。");
        ShowWindow(MainWindow);
        MainWindowVisibilityChanged?.Invoke(IsMainWindowVisible);
    }

    public void ShowQuickSearchWindow()
    {
        Log.Debug("请求显示快速检索窗口。");
        ShowWindow(QuickSearchWindow);
        QuickSearchWindow?.FocusSearchBox();
        QuickSearchWindowVisibilityChanged?.Invoke(IsQuickSearchWindowVisible);
    }

    public void ToggleQuickSearchWindow()
    {
        if (QuickSearchWindow is null)
        {
            Log.Debug("快速检索窗口未创建，跳过切换。");
            return;
        }

        if (QuickSearchWindow.IsVisible)
        {
            Log.Debug("快速检索窗口当前可见，准备隐藏。");
            HideQuickSearchWindow();
            return;
        }

        Log.Debug("快速检索窗口当前隐藏，准备显示。");
        ShowQuickSearchWindow();
    }

    public void FocusQuickSearchWindow()
    {
        Log.Debug("请求聚焦快速检索窗口。");
        QuickSearchWindow?.FocusSearchBox();
    }

    public void HideMainWindow()
    {
        Log.Debug("请求隐藏主窗口。");
        HideWindow(MainWindow);
        MainWindowVisibilityChanged?.Invoke(IsMainWindowVisible);
    }

    public void HideQuickSearchWindow()
    {
        Log.Debug("请求隐藏快速检索窗口。");
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
