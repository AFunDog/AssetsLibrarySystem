using System;
using System.IO;
using System.Threading.Tasks;
using AssetsLibrarySystem.Application.Infrastructure;
using AssetsLibrarySystem.Application.Services.AssetDescription;
using AssetsLibrarySystem.Application.Services.AssetLibrary;
using AssetsLibrarySystem.Application.Services.AssetSearch;
using AssetsLibrarySystem.Application.Services.BackendLauncher;
using AssetsLibrarySystem.Application.Services.BackgroundTasks;
using AssetsLibrarySystem.Application.Services.Infrastructure;
using AssetsLibrarySystem.Application.UseCases.AssetOperations;
using Autofac;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace AssetsLibrarySystem.ConsoleHost;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        ConfigureLogger();

        try
        {
            var builder = new ContainerBuilder();
            builder.RegisterInstance(ApplicationConfigurationFactory.CreateConfiguration())
                .As<IConfiguration>()
                .SingleInstance();
            builder.RegisterType<ConfigurationSearchModelOptionsProvider>()
                .As<ISearchModelOptionsProvider>()
                .SingleInstance();
            builder.RegisterType<DatabaseWriteQueue>()
                .As<IDatabaseWriteQueue>()
                .SingleInstance();
            builder.RegisterType<SqliteAssetDatabase>()
                .As<IAssetDatabase>()
                .SingleInstance();
            builder.RegisterType<AssetLibraryService>()
                .As<IAssetLibraryService>()
                .SingleInstance();
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
            builder.RegisterType<AssetSearchService>()
                .As<IAssetSearchService>()
                .SingleInstance();
            builder.RegisterType<BackendLauncherService>()
                .As<IBackendLauncher>()
                .SingleInstance();
            builder.RegisterType<BackgroundTaskService>()
                .As<IBackgroundTaskService>()
                .SingleInstance();
            builder.RegisterType<DescribeAssetsUseCase>()
                .AsSelf()
                .SingleInstance();
            builder.RegisterType<VectorizeDescriptionsUseCase>()
                .AsSelf()
                .SingleInstance();
            builder.RegisterType<DeleteAssetDescriptionUseCase>()
                .AsSelf()
                .SingleInstance();
            builder.RegisterType<RebuildSearchIndexUseCase>()
                .AsSelf()
                .SingleInstance();
            var container = builder.Build();
            using var scope = container;

            var runner = new ConsoleCommandRunner(
                scope.Resolve<IAssetLibraryService>(),
                scope.Resolve<IAssetDescriptionStore>(),
                scope.Resolve<IAssetDescriptionVectorStore>(),
                scope.Resolve<IAssetSearchService>(),
                scope.Resolve<IBackendLauncher>(),
                scope.Resolve<DescribeAssetsUseCase>(),
                scope.Resolve<VectorizeDescriptionsUseCase>(),
                scope.Resolve<RebuildSearchIndexUseCase>());

            return await runner.RunAsync(args);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "控制台执行失败");
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static void ConfigureLogger()
    {
        var baseDir = AppContext.BaseDirectory;
        var loggerConfig = new ConfigurationBuilder()
            .SetBasePath(baseDir)
            .AddJsonFile(Path.Combine(baseDir, "serilog.json"), optional: true, reloadOnChange: false)
            .AddEnvironmentVariables(prefix: "ALS_")
            .Build();

        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(loggerConfig)
            .CreateLogger();
    }
}
