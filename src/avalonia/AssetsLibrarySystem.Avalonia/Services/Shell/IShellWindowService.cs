using System;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace AssetsLibrarySystem.Avalonia.Services.Shell;

public interface IShellWindowService
{
    event Action<bool>? MainWindowVisibilityChanged;
    event Action<bool>? QuickSearchWindowVisibilityChanged;

    bool IsMainWindowVisible { get; }
    bool IsQuickSearchWindowVisible { get; }

    void AttachDesktop(IClassicDesktopStyleApplicationLifetime desktop);
    void AttachMainWindow(Window window);
    void AttachQuickSearchWindow(Window window);

    void SetShuttingDown(bool isShuttingDown);
    void RequestShutdown();
    void ShowMainWindow();
    void ShowQuickSearchWindow();
    void ToggleQuickSearchWindow();
    void FocusQuickSearchWindow();
    void HideMainWindow();
    void HideQuickSearchWindow();
}