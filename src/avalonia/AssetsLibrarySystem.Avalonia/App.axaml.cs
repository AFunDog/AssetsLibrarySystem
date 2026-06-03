using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using AssetsLibrarySystem.Application.Infrastructure;
using AssetsLibrarySystem.Avalonia.Services.Activity;
using AssetsLibrarySystem.Avalonia.Services.Backend;
using AssetsLibrarySystem.Avalonia.Services.Library;
using AssetsLibrarySystem.Avalonia.Services.Shell;
using AssetsLibrarySystem.Avalonia.Services.Settings;
using AssetsLibrarySystem.Avalonia.ViewModels;
using AssetsLibrarySystem.Avalonia.Views;
using Autofac;
using Serilog;

namespace AssetsLibrarySystem.Avalonia;

public partial class App : global::Avalonia.Application
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
            Log.Information("开始初始化桌面生命周期，ShutdownMode=OnExplicitShutdown。");

            BuildContainer();
            var shellWindowService = Container!.Resolve<IShellWindowService>();
            var viewModel = Container!.Resolve<MainWindowViewModel>();
            var quickSearchViewModel = Container!.Resolve<QuickSearchViewModel>();

            ShellViewModel = Container!.Resolve<DesktopShellViewModel>();
            DataContext = ShellViewModel;
            shellWindowService.AttachDesktop(desktop);

            var mainWindow = new MainWindow
            {
                DataContext = viewModel,
            };

            var quickSearchWindow = new QuickSearchWindow
            {
                DataContext = quickSearchViewModel,
            };

            desktop.MainWindow = mainWindow;
            shellWindowService.AttachMainWindow(mainWindow);
            shellWindowService.AttachQuickSearchWindow(quickSearchWindow);

            desktop.Exit += (_, _) =>
            {
                Log.Information("桌面生命周期触发 Exit 事件，开始统一清理。");
                ShutdownDesktop(shellWindowService);
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

    private void BuildContainer()
    {
        Log.Debug("开始构建 Avalonia 依赖容器。");
        var builder = ServiceBootstrapper.CreateBuilder();
        builder.RegisterType<ShellWindowService>()
            .As<IShellWindowService>()
            .SingleInstance();
        builder.RegisterType<DesktopShellViewModel>()
            .AsSelf()
            .SingleInstance();
        builder.RegisterType<QuickSearchViewModel>()
            .AsSelf()
            .SingleInstance();
        builder.RegisterType<ActivityFeedService>()
            .AsSelf()
            .SingleInstance();
        builder.RegisterType<UserSettingsService>()
            .As<IUserSettingsService>()
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
        Log.Debug("Avalonia 依赖容器构建完成。");
    }

    private void ShutdownDesktop(IShellWindowService shellWindowService)
    {
        try
        {
            Log.Information("开始清理桌面端资源。");
            ShellViewModel?.Dispose();
            shellWindowService.SetShuttingDown(true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "桌面端退出时清理 Python 后端失败");
        }
        finally
        {
            Container?.Dispose();
            Log.Information("桌面端资源清理完成。");
        }
    }
}
