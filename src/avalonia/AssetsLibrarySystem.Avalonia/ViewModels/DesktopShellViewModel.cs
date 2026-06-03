using System;
using Avalonia.Threading;
using AssetsLibrarySystem.Avalonia.Services.BackendLauncher;
using AssetsLibrarySystem.Avalonia.Services.Hotkey;
using AssetsLibrarySystem.Avalonia.Services.Shell;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AssetsLibrarySystem.Avalonia.ViewModels;

public partial class DesktopShellViewModel : ObservableObject, IDisposable
{
    private IShellWindowService ShellWindowService { get; }
    private IBackendLauncher? BackendLauncher { get; set; }
    private GlobalHotkeyService? HotkeyService { get; set; }
    private bool IsDisposed { get; set; }

    public DesktopShellViewModel()
        : this(new ShellWindowService(), null)
    {
    }

    public DesktopShellViewModel(IShellWindowService shellWindowService)
        : this(shellWindowService, null)
    {
    }

    public DesktopShellViewModel(IShellWindowService shellWindowService, IBackendLauncher? backendLauncher)
    {
        ShellWindowService = shellWindowService;
        BackendLauncher = backendLauncher;
        TrayTitle = "Assets Library System";
        TrayStatusTitle = "Python 模型服务待连接";
        TrayStatusDetail = "快捷键 Ctrl+Shift+Space 打开快速检索";
        TrayHotkeyHint = "快速检索：Ctrl+Shift+Space（Windows）";
        TrayActionHint = "左键托盘打开快速检索，右键查看菜单。";
        ShellWindowService.MainWindowVisibilityChanged += OnMainWindowVisibilityChanged;
        ShellWindowService.QuickSearchWindowVisibilityChanged += OnQuickSearchWindowVisibilityChanged;
        IsMainWindowVisible = ShellWindowService.IsMainWindowVisible;
        IsQuickSearchVisible = ShellWindowService.IsQuickSearchWindowVisible;
        RefreshTrayStatus();
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

    public void AttachBackendLauncher(IBackendLauncher? backendLauncher)
    {
        BackendLauncher = backendLauncher;
        RefreshTrayStatus();
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
        ShellWindowService.ShowMainWindow();
        TrayActionHint = "已打开主窗口。";
    }

    [RelayCommand]
    private void ShowQuickSearchWindow()
    {
        ShellWindowService.ShowQuickSearchWindow();
        TrayActionHint = "快速检索窗口已打开。";
    }

    [RelayCommand]
    private void ToggleQuickSearchWindow()
    {
        ShellWindowService.ToggleQuickSearchWindow();
        TrayActionHint = ShellWindowService.IsQuickSearchWindowVisible
            ? "快速检索窗口已打开。"
            : "快速检索窗口已隐藏。";
    }

    [RelayCommand]
    private void ExitApplication()
    {
        IsShuttingDown = true;
        ShellWindowService.SetShuttingDown(true);
        TrayActionHint = "正在退出应用。";
        ShellWindowService.RequestShutdown();
    }

    public void Dispose()
    {
        if (IsDisposed)
        {
            return;
        }

        IsDisposed = true;
        ShellWindowService.MainWindowVisibilityChanged -= OnMainWindowVisibilityChanged;
        ShellWindowService.QuickSearchWindowVisibilityChanged -= OnQuickSearchWindowVisibilityChanged;
        HotkeyService?.Dispose();
    }

    private void OnHotkeyPressed(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            ShellWindowService.ShowQuickSearchWindow();
            TrayActionHint = "快速检索窗口已打开。";
        });
    }

    private void OnMainWindowVisibilityChanged(bool isVisible)
    {
        IsMainWindowVisible = isVisible;
        if (!IsShuttingDown)
        {
            TrayActionHint = isVisible ? "已打开主窗口。" : "主窗口已隐藏到托盘。";
        }
    }

    private void OnQuickSearchWindowVisibilityChanged(bool isVisible)
    {
        IsQuickSearchVisible = isVisible;
        if (!IsShuttingDown)
        {
            TrayActionHint = isVisible ? "快速检索窗口已打开。" : "快速检索窗口已隐藏。";
        }
    }
}
