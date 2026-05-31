using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using AssetsLibrarySystem.Avalonia.Services.BackendLauncher;
using AssetsLibrarySystem.Avalonia.ViewModels;
using AssetsLibrarySystem.Avalonia.Views;
using Autofac;

namespace AssetsLibrarySystem.Avalonia;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override async void OnFrameworkInitializationCompleted()
    {
        // 3. 从容器解析 ViewModel，创建主窗口
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = ServiceBuilder.Services.Resolve<MainWindowViewModel>(),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
    
}
