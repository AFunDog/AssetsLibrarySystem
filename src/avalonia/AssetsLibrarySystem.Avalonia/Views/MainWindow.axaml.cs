using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using AssetsLibrarySystem.Avalonia;

namespace AssetsLibrarySystem.Avalonia.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Closing += MainWindow_Closing;
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

    private void MainWindow_Closing(object? sender, WindowClosingEventArgs e)
    {
        if (Application.Current is App app && app.ShellViewModel?.IsShuttingDown == false)
        {
            e.Cancel = true;
            Hide();
        }
    }
}
