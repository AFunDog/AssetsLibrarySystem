using System;
using System.IO;
using AssetsLibrarySystem.Application.Infrastructure;
using AssetsLibrarySystem.Application.Models;
using AssetsLibrarySystem.Application.Services.AssetDescription;
using AssetsLibrarySystem.Application.Services.AssetLibrary;
using AssetsLibrarySystem.Application.Services.AssetSearch;
using AssetsLibrarySystem.Application.Services.BackendApi;
using AssetsLibrarySystem.Application.Services.BackendLauncher;
using AssetsLibrarySystem.Application.Services.BackgroundTasks;
using AssetsLibrarySystem.Application.Services.Infrastructure;
using AssetsLibrarySystem.Application.Services.Python;
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
        builder.RegisterModule<PythonModule>();
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

public sealed class PythonModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        var backendSourcePath = ResolveBackendSourcePath();

        builder.Register(c => new PythonEngineService(backendSourcePath))
            .AsSelf()
            .As<IBackendLauncher>()
            .SingleInstance()
            .OnRelease(engine => engine.Dispose());

        builder.RegisterType<PythonModelService>()
            .As<IBackendModelClient>()
            .SingleInstance();

        builder.RegisterType<PythonSearchService>()
            .As<IBackendSearchClient>()
            .SingleInstance();
    }

    private static string ResolveBackendSourcePath()
    {
        var baseDir = AppContext.BaseDirectory;
        var current = new DirectoryInfo(baseDir);
        while (current != null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "src", "backend", "app")))
            {
                return Path.Combine(current.FullName, "src", "backend");
            }
            current = current.Parent;
        }
        throw new InvalidOperationException("无法找到 Python 后端源码目录 (src/backend/app)");
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

        // 角度配置管理
        builder.Register(_ =>
            {
                // 优先从输出目录查找，回退到源码目录
                var yamlPath = Path.Combine(AppContext.BaseDirectory, "angle_profiles.yaml");
                if (!File.Exists(yamlPath))
                {
                    // 回退到仓库源路径
                    var repoRoot = SharedDataPathHelper.GetRepositoryRoot();
                    yamlPath = Path.Combine(repoRoot, "src", "avalonia", "AssetsLibrarySystem.Application", "angle_profiles.yaml");
                }
                return new AngleProfileManager(yamlPath);
            })
            .AsSelf()
            .SingleInstance();

        // 子类型检测
        builder.RegisterType<SubtypeDetector>()
            .As<ISubtypeDetector>()
            .SingleInstance();
    }
}

public sealed class AssetSearchModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<SearchParameterNormalizer>()
            .As<ISearchParameterNormalizer>()
            .SingleInstance();

        builder.RegisterType<AssetFormatResolver>()
            .As<IAssetFormatResolver>()
            .SingleInstance();

        builder.RegisterType<VectorRecordRepository>()
            .As<IVectorRecordRepository>()
            .SingleInstance();

        builder.RegisterType<QueryEmbeddingClient>()
            .As<IQueryEmbeddingClient>()
            .SingleInstance();

        builder.RegisterType<RerankClient>()
            .As<IRerankClient>()
            .SingleInstance();

        builder.RegisterType<ExactVectorRetriever>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<HnswVectorRetriever>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<VectorRetrieverSelector>()
            .As<IVectorCandidateRetriever>()
            .SingleInstance();

        builder.RegisterType<RerankCandidateSelector>()
            .As<IRerankCandidateSelector>()
            .SingleInstance();

        builder.RegisterType<ScoreFusionService>()
            .As<IScoreFusionService>()
            .SingleInstance();

        builder.RegisterType<SearchResultAggregator>()
            .As<ISearchResultAggregator>()
            .SingleInstance();

        builder.RegisterType<AssetSearchPipeline>()
            .As<IAssetSearchPipeline>()
            .SingleInstance();

        builder.RegisterType<AssetSearchService>()
            .As<IAssetSearchService>()
            .SingleInstance();
    }
}

public sealed class BackgroundTaskModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        // Avalonia 可覆盖为 Dispatcher 调度；默认同步执行（Console/测试）。
        builder.RegisterType<InlineBackgroundTaskUiScheduler>()
            .As<IBackgroundTaskUiScheduler>()
            .SingleInstance()
            .IfNotRegistered(typeof(IBackgroundTaskUiScheduler));

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