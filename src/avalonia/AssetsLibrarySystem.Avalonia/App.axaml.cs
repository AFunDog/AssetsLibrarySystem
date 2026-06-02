using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using AssetsLibrarySystem.Avalonia.Infrastructure;
using AssetsLibrarySystem.Avalonia.Services.BackendLauncher;
using AssetsLibrarySystem.Avalonia.Services.Activity;
using AssetsLibrarySystem.Avalonia.Services.Backend;
using AssetsLibrarySystem.Avalonia.Services.Library;
using AssetsLibrarySystem.Avalonia.ViewModels;
using AssetsLibrarySystem.Avalonia.Views;
using Autofac;
using Serilog;

namespace AssetsLibrarySystem.Avalonia;

public partial class App : Application
{
    private ILifetimeScope? Container { get; set; }
    public DesktopShellViewModel? ShellViewModel { get; private set; }

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
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var builder = ServiceBootstrapper.CreateBuilder();
            builder.RegisterType<DesktopShellViewModel>()
                .AsSelf()
                .SingleInstance();
            builder.RegisterType<QuickSearchViewModel>()
                .AsSelf()
                .SingleInstance();
            builder.RegisterType<ActivityFeedService>()
                .AsSelf()
                .SingleInstance();
            builder.RegisterType<BackendSessionService>()
                .AsSelf()
                .SingleInstance();
            builder.RegisterType<LibraryCatalogService>()
                .AsSelf()
                .SingleInstance();
            builder.RegisterType<OverviewPageViewModel>()
                .AsSelf()
                .SingleInstance();
            builder.RegisterType<LibraryPageViewModel>()
                .AsSelf()
                .SingleInstance();
            builder.RegisterType<DescriptionTasksPageViewModel>()
                .AsSelf()
                .SingleInstance();
            builder.RegisterType<SettingsPageViewModel>()
                .AsSelf()
                .SingleInstance();
            builder.RegisterType<MainWindowViewModel>()
                .AsSelf()
                .SingleInstance();
            Container = builder.Build();

            ShellViewModel = Container.Resolve<DesktopShellViewModel>();
            var viewModel = Container.Resolve<MainWindowViewModel>();
            var quickSearchViewModel = Container.Resolve<QuickSearchViewModel>();
            var backendLauncher = Container.ResolveOptional<IBackendLauncher>();

            DataContext = ShellViewModel;
            ShellViewModel.AttachDesktop(desktop);
            ShellViewModel.AttachBackendLauncher(backendLauncher);

            var mainWindow = new MainWindow
            {
                DataContext = viewModel,
            };

            var quickSearchWindow = new QuickSearchWindow
            {
                DataContext = quickSearchViewModel,
            };

            desktop.MainWindow = mainWindow;
            ShellViewModel.AttachMainWindow(mainWindow);
            ShellViewModel.AttachQuickSearchWindow(quickSearchWindow);

            desktop.Exit += (_, _) =>
            {
                try
                {
                    ShellViewModel?.Dispose();
                    if (backendLauncher is not null)
                    {
                        backendLauncher.DisposeAsync().AsTask().GetAwaiter().GetResult();
                        Log.Information("桌面端退出时已清理 Python 后端进程");
                    }
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

            ShellViewModel.StartHotkey();
            Log.Information("主窗口已创建，开始初始化视图模型");

            try
            {
                await viewModel.InitializeAsync();
                ShellViewModel.RefreshTrayStatus();
                Log.Information("主窗口视图模型已准备就绪");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "主窗口初始化失败");
                ShellViewModel.RefreshTrayStatus();
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
