using System;
using AssetsLibrarySystem.Application.Services.AssetDescription;
using AssetsLibrarySystem.Application.Services.AssetLibrary;
using AssetsLibrarySystem.Application.Services.AssetSearch;
using AssetsLibrarySystem.Application.Services.BackendLauncher;
using AssetsLibrarySystem.Application.Services.BackgroundTasks;
using AssetsLibrarySystem.Application.Services.Infrastructure;
using Autofac;

namespace AssetsLibrarySystem.Application.DependencyInjection;

public sealed class ApplicationModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterModule<ApplicationInfrastructureModule>();
        builder.RegisterModule<AssetLibraryModule>();
        builder.RegisterModule<AssetDescriptionModule>();
        builder.RegisterModule<AssetSearchModule>();
        builder.RegisterModule<BackendModule>();
        builder.RegisterModule<BackgroundTaskModule>();
        builder.RegisterModule<ApplicationUseCaseModule>();
    }
}

public sealed class ApplicationInfrastructureModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<DatabaseWriteQueue>()
            .As<IDatabaseWriteQueue>()
            .SingleInstance();

        builder.RegisterType<SqliteAssetDatabase>()
            .As<IAssetDatabase>()
            .SingleInstance();
    }
}

public sealed class AssetLibraryModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<AssetLibraryService>()
            .As<IAssetLibraryService>()
            .SingleInstance();
    }
}

public sealed class AssetDescriptionModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<AssetDescriptionStore>()
            .As<IAssetDescriptionStore>()
            .SingleInstance();

        builder.RegisterType<AssetDescriptionVectorStore>()
            .As<IAssetDescriptionVectorStore>()
            .SingleInstance();

        builder.RegisterType<AssetDescriptionService>()
            .As<IAssetDescriptionService>()
            .SingleInstance();

        builder.RegisterType<AssetTextVectorizationService>()
            .As<IAssetTextVectorizationService>()
            .SingleInstance();
    }
}

public sealed class AssetSearchModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<AssetSearchService>()
            .As<IAssetSearchService>()
            .SingleInstance();
    }
}

public sealed class BackendModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<BackendLauncherService>()
            .As<IBackendLauncher>()
            .SingleInstance();
    }
}

public sealed class BackgroundTaskModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<BackgroundTaskService>()
            .As<IBackgroundTaskService>()
            .SingleInstance();
    }
}

public sealed class ApplicationUseCaseModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        var assembly = typeof(ApplicationUseCaseModule).Assembly;

        builder.RegisterAssemblyTypes(assembly)
            .Where(type => type.IsClass && !type.IsAbstract && type.Name.EndsWith("UseCase", StringComparison.Ordinal))
            .AsSelf()
            .SingleInstance();
    }
}
