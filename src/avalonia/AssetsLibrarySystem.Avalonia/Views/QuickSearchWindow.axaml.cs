using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using AssetsLibrarySystem.Avalonia;
using AssetsLibrarySystem.Avalonia.ViewModels;
using CommunityToolkit.Mvvm.Input;

namespace AssetsLibrarySystem.Avalonia.Views;

public partial class QuickSearchWindow : Window
{
    public QuickSearchWindow()
    {
        InitializeComponent();
        Opened += (_, _) => FocusSearchBox();
        Closing += QuickSearchWindow_Closing;
    }

    public void FocusSearchBox()
    {
        SearchTextBox.Focus();
        SearchTextBox.SelectAll();
    }

    private void QuickSearchWindow_Closing(object? sender, WindowClosingEventArgs e)
    {
        if (Application.Current is App app && app.ShellViewModel?.IsShuttingDown == false)
        {
            e.Cancel = true;
            Hide();
        }
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
}
