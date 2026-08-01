using System;
using AssetsLibrarySystem.Application.Infrastructure;
using AssetsLibrarySystem.Application.Services.AssetLibrary;
using AssetsLibrarySystem.Application.Services.BackgroundTasks;
using AssetsLibrarySystem.Avalonia.Services.Activity;
using AssetsLibrarySystem.Avalonia.Services.Backend;
using AssetsLibrarySystem.Avalonia.Services.Search;
using AssetsLibrarySystem.Avalonia.Services.Settings;
using AssetsLibrarySystem.Avalonia.Services.Thumbnail;
using AssetsLibrarySystem.Avalonia.Services.Shell;
using Autofac;
using Avalonia.Threading;

namespace AssetsLibrarySystem.Avalonia.DependencyInjection;

public sealed class AvaloniaModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterModule<AvaloniaServiceModule>();
        builder.RegisterModule<AvaloniaShellModule>();
        builder.RegisterModule<AvaloniaViewModelModule>();
    }
}

public sealed class AvaloniaServiceModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<AvaloniaBackgroundTaskUiScheduler>()
            .As<IBackgroundTaskUiScheduler>()
            .SingleInstance();

        builder.RegisterType<ActivityFeedService>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<ThumbnailCacheService>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<SearchHistoryService>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<BackendSessionService>()
            .As<IBackendSessionService>()
            .SingleInstance();

        builder.RegisterType<UserSettingsService>()
            .As<IUserSettingsService>()
            .As<ISearchModelOptionsProvider>()
            .SingleInstance();
    }
}

/// <summary>将后台任务状态变更派发到 Avalonia UI 线程。</summary>
file sealed class AvaloniaBackgroundTaskUiScheduler : IBackgroundTaskUiScheduler
{
    public void Schedule(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }

        Dispatcher.UIThread.Post(action, DispatcherPriority.Background);
    }
}

public sealed class AvaloniaShellModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<ShellWindowService>()
            .As<IShellWindowService>()
            .SingleInstance();

        // 注册窗口类型，由 DI 自动装配
        builder.RegisterType<Views.MainWindow>()
            .AsSelf()
            .SingleInstance();
        builder.RegisterType<Views.QuickSearchWindow>()
            .AsSelf()
            .SingleInstance();
    }
}

public sealed class AvaloniaViewModelModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        var assembly = typeof(AvaloniaViewModelModule).Assembly;

        builder.RegisterAssemblyTypes(assembly)
            .Where(type => type.IsClass && !type.IsAbstract && type.Name.EndsWith("ViewModel", StringComparison.Ordinal))
            .AsSelf()
            .SingleInstance();
    }
}
