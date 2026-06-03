using System;
using Avalonia.Controls.ApplicationLifetimes;
using AssetsLibrarySystem.Avalonia.Views;

namespace AssetsLibrarySystem.Avalonia.Services.Shell;

public interface IShellWindowService
{
    event Action<bool>? MainWindowVisibilityChanged;
    event Action<bool>? QuickSearchWindowVisibilityChanged;

    bool IsMainWindowVisible { get; }
    bool IsQuickSearchWindowVisible { get; }

    void AttachDesktop(IClassicDesktopStyleApplicationLifetime desktop);
    void AttachMainWindow(MainWindow window);
    void AttachQuickSearchWindow(QuickSearchWindow window);

    void SetShuttingDown(bool isShuttingDown);
    void RequestShutdown();
    void ShowMainWindow();
    void ShowQuickSearchWindow();
    void ToggleQuickSearchWindow();
    void FocusQuickSearchWindow();
    void HideMainWindow();
    void HideQuickSearchWindow();
}
