using System;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using AssetsLibrarySystem.Avalonia.Services.BackendLauncher;
using AssetsLibrarySystem.Avalonia.Services.Hotkey;
using AssetsLibrarySystem.Avalonia.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace AssetsLibrarySystem.Avalonia.ViewModels;

public partial class DesktopShellViewModel : ObservableObject, IDisposable
{
    private IBackendLauncher? BackendLauncher { get; set; }
    private IClassicDesktopStyleApplicationLifetime? Desktop { get; set; }
    private GlobalHotkeyService? HotkeyService { get; set; }
    private MainWindow? MainWindow { get; set; }
    private QuickSearchWindow? QuickSearchWindow { get; set; }
    private bool IsDisposed { get; set; }

    public DesktopShellViewModel()
    {
        TrayTitle = "Assets Library System";
        TrayStatusTitle = "Python 模型服务待连接";
        TrayStatusDetail = "快捷键 Ctrl+Shift+Space 打开快速检索";
        TrayHotkeyHint = "快速检索：Ctrl+Shift+Space（Windows）";
        TrayActionHint = "左键托盘打开快速检索，右键查看菜单。";
    }

    [ObservableProperty]
    public partial string TrayTitle { get; set; }

    [ObservableProperty]
    public partial string TrayStatusTitle { get; set; }

    [ObservableProperty]
    public partial string TrayStatusDetail { get; set; }

    [ObservableProperty]
    public partial string TrayHotkeyHint { get; set; }

    [ObservableProperty]
    public partial string TrayActionHint { get; set; }

    [ObservableProperty]
    public partial bool IsShuttingDown { get; set; }

    [ObservableProperty]
    public partial bool IsMainWindowVisible { get; set; }

    [ObservableProperty]
    public partial bool IsQuickSearchVisible { get; set; }

    public void AttachDesktop(IClassicDesktopStyleApplicationLifetime desktop)
    {
        Desktop = desktop;
    }

    public void AttachBackendLauncher(IBackendLauncher? backendLauncher)
    {
        BackendLauncher = backendLauncher;
        RefreshTrayStatus();
    }

    public void AttachMainWindow(MainWindow window)
    {
        MainWindow = window;
        window.Closing += MainWindow_Closing;
        IsMainWindowVisible = window.IsVisible;
    }

    public void AttachQuickSearchWindow(QuickSearchWindow window)
    {
        QuickSearchWindow = window;
        window.Closing += QuickSearchWindow_Closing;
        IsQuickSearchVisible = window.IsVisible;
    }

    public void StartHotkey()
    {
        if (HotkeyService is not null)
        {
            return;
        }

        HotkeyService = new GlobalHotkeyService();
        HotkeyService.HotkeyPressed += OnHotkeyPressed;
        HotkeyService.Start();
        TrayActionHint = "左键托盘打开快速检索，右键查看菜单。";
    }

    public void RefreshTrayStatus()
    {
        TrayStatusTitle = BackendLauncher?.IsRunning == true
            ? "Python 模型服务已连接"
            : "Python 模型服务未运行";
        TrayStatusDetail = BackendLauncher?.BaseUrl ?? "http://127.0.0.1:8000";
    }

    [RelayCommand]
    private void ShowMainWindow()
    {
        if (MainWindow is null)
        {
            return;
        }

        ShowWindow(MainWindow);
        IsMainWindowVisible = true;
        TrayActionHint = "已打开主窗口。";
    }

    [RelayCommand]
    private void ShowQuickSearchWindow()
    {
        if (QuickSearchWindow is null)
        {
            return;
        }

        ShowWindow(QuickSearchWindow);
        QuickSearchWindow.FocusSearchBox();
        IsQuickSearchVisible = true;
        TrayActionHint = "快速检索窗口已打开。";
    }

    [RelayCommand]
    private void ToggleQuickSearchWindow()
    {
        if (QuickSearchWindow is null)
        {
            return;
        }

        if (QuickSearchWindow.IsVisible)
        {
            QuickSearchWindow.Hide();
            IsQuickSearchVisible = false;
            TrayActionHint = "快速检索窗口已隐藏。";
            return;
        }

        ShowQuickSearchWindow();
    }

    [RelayCommand]
    private void ExitApplication()
    {
        IsShuttingDown = true;
        TrayActionHint = "正在退出应用。";
        Desktop?.Shutdown();
    }

    public void Dispose()
    {
        if (IsDisposed)
        {
            return;
        }

        IsDisposed = true;
        HotkeyService?.Dispose();
    }

    private void MainWindow_Closing(object? sender, WindowClosingEventArgs e)
    {
        if (IsShuttingDown)
        {
            return;
        }

        e.Cancel = true;
        HideWindow(MainWindow);
        IsMainWindowVisible = false;
        TrayActionHint = "主窗口已隐藏到托盘。";
    }

    private void QuickSearchWindow_Closing(object? sender, WindowClosingEventArgs e)
    {
        if (IsShuttingDown)
        {
            return;
        }

        e.Cancel = true;
        HideWindow(QuickSearchWindow);
        IsQuickSearchVisible = false;
        TrayActionHint = "快速检索窗口已隐藏。";
    }

    private void OnHotkeyPressed(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(ShowQuickSearchWindow);
    }

    private static void ShowWindow(Window window)
    {
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
