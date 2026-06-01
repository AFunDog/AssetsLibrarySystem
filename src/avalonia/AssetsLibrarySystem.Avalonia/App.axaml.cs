using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using AssetsLibrarySystem.Avalonia.Infrastructure;
using AssetsLibrarySystem.Avalonia.Services.BackendLauncher;
using AssetsLibrarySystem.Avalonia.ViewModels;
using AssetsLibrarySystem.Avalonia.Views;
using Autofac;
using Serilog;

namespace AssetsLibrarySystem.Avalonia;

public partial class App : Application
{
    private ILifetimeScope? Container { get; set; }

    public override void Initialize()
    {
        Log.Information("初始化 Avalonia XAML");
        AvaloniaXamlLoader.Load(this);
#if DEBUG
        this.AttachDeveloperTools();
#endif
    }

    public override async void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var builder = ServiceBootstrapper.CreateBuilder();
            builder.RegisterType<MainWindowViewModel>()
                .AsSelf()
                .InstancePerDependency();
            Container = builder.Build();

            var viewModel = Container.Resolve<MainWindowViewModel>();
            var backendLauncher = Container.ResolveOptional<IBackendLauncher>();

            desktop.MainWindow = new MainWindow
            {
                DataContext = viewModel,
            };

            desktop.Exit += async (_, _) =>
            {
                if (backendLauncher is null)
                {
                    Container?.Dispose();
                    return;
                }

                try
                {
                    await backendLauncher.StopAsync();
                    await backendLauncher.DisposeAsync();
                    Log.Information("桌面端退出时已清理 Python 后端进程");
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "桌面端退出时清理 Python 后端失败");
                }
                finally
                {
                    Container?.Dispose();
                }
            };

            Log.Information("主窗口已创建，开始初始化视图模型");

            try
            {
                await viewModel.InitializeAsync();
                Log.Information("主窗口视图模型已准备就绪");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "主窗口初始化失败");
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
