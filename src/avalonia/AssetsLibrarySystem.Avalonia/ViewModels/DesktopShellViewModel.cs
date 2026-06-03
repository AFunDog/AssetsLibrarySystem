using System;
using Avalonia.Threading;
using AssetsLibrarySystem.Application.Services.BackendLauncher;
using AssetsLibrarySystem.Avalonia.Services.Hotkey;
using AssetsLibrarySystem.Avalonia.Services.Shell;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

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
        Log.Debug("DesktopShellViewModel 已创建，backendLauncherRegistered={HasBackendLauncher}", BackendLauncher is not null);
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
        Log.Information("桌面壳层已绑定后端启动器，hasBackendLauncher={HasBackendLauncher}", BackendLauncher is not null);
        RefreshTrayStatus();
    }

    public void StartHotkey()
    {
        if (HotkeyService is not null)
        {
            Log.Debug("全局快捷键服务已存在，跳过重复启动。");
            return;
        }

        Log.Information("开始注册全局快捷键。");
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
        Log.Debug(
            "刷新托盘状态: backendRunning={BackendRunning}, baseUrl={BaseUrl}",
            BackendLauncher?.IsRunning == true,
            TrayStatusDetail);
    }

    [RelayCommand]
    private void ShowMainWindow()
    {
        Log.Information("用户操作: 打开主窗口。");
        ShellWindowService.ShowMainWindow();
        TrayActionHint = "已打开主窗口。";
    }

    [RelayCommand]
    private void ShowQuickSearchWindow()
    {
        Log.Information("用户操作: 打开快速检索窗口。");
        ShellWindowService.ShowQuickSearchWindow();
        TrayActionHint = "快速检索窗口已打开。";
    }

    [RelayCommand]
    private void ToggleQuickSearchWindow()
    {
        Log.Information("用户操作: 切换快速检索窗口显示状态。");
        ShellWindowService.ToggleQuickSearchWindow();
        TrayActionHint = ShellWindowService.IsQuickSearchWindowVisible
            ? "快速检索窗口已打开。"
            : "快速检索窗口已隐藏。";
    }

    [RelayCommand]
    private void ExitApplication()
    {
        Log.Information("用户操作: 托盘退出应用。");
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
        Log.Debug("DesktopShellViewModel 正在释放。");
        ShellWindowService.MainWindowVisibilityChanged -= OnMainWindowVisibilityChanged;
        ShellWindowService.QuickSearchWindowVisibilityChanged -= OnQuickSearchWindowVisibilityChanged;
        HotkeyService?.Dispose();
    }

    private void OnHotkeyPressed(object? sender, EventArgs e)
    {
        Log.Information("用户触发全局热键，打开快速检索窗口。");
        Dispatcher.UIThread.Post(() =>
        {
            ShellWindowService.ShowQuickSearchWindow();
            TrayActionHint = "快速检索窗口已打开。";
        });
    }

    private void OnMainWindowVisibilityChanged(bool isVisible)
    {
        IsMainWindowVisible = isVisible;
        Log.Debug("主窗口可见性变更: isVisible={IsVisible}", isVisible);
        if (!IsShuttingDown)
        {
            TrayActionHint = isVisible ? "已打开主窗口。" : "主窗口已隐藏到托盘。";
        }
    }

    private void OnQuickSearchWindowVisibilityChanged(bool isVisible)
    {
        IsQuickSearchVisible = isVisible;
        Log.Debug("快速检索窗口可见性变更: isVisible={IsVisible}", isVisible);
        if (!IsShuttingDown)
        {
            TrayActionHint = isVisible ? "快速检索窗口已打开。" : "快速检索窗口已隐藏。";
        }
    }
}
