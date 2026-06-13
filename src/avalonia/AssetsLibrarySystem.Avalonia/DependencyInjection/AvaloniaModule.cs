using System;
using AssetsLibrarySystem.Application.Infrastructure;
using AssetsLibrarySystem.Avalonia.Services.Activity;
using AssetsLibrarySystem.Avalonia.Services.Backend;
using AssetsLibrarySystem.Avalonia.Services.Library;
using AssetsLibrarySystem.Avalonia.Services.Settings;
using AssetsLibrarySystem.Avalonia.Services.Shell;
using Autofac;

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
        builder.RegisterType<ActivityFeedService>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<BackendSessionService>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<LibraryCatalogService>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<UserSettingsService>()
            .As<IUserSettingsService>()
            .As<ISearchModelOptionsProvider>()
            .SingleInstance();
    }
}

public sealed class AvaloniaShellModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<ShellWindowService>()
            .As<IShellWindowService>()
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
