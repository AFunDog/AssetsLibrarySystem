using System;
using System.Collections.Generic;
using System.IO;
using Autofac;
using AssetsLibrarySystem.Avalonia.Services.AssetDescription;
using AssetsLibrarySystem.Avalonia.Services.AssetLibrary;
using AssetsLibrarySystem.Avalonia.Services.BackendLauncher;
using Microsoft.Extensions.Configuration;

namespace AssetsLibrarySystem.Avalonia.Infrastructure;

public static class ServiceBootstrapper
{
    public static ContainerBuilder CreateBuilder()
    {
        var builder = new ContainerBuilder();
        builder.RegisterInstance(CreateConfiguration()).As<IConfiguration>().SingleInstance();
        builder.RegisterType<AssetLibraryService>().As<IAssetLibraryService>().SingleInstance();
        builder.RegisterType<AssetDescriptionStore>().As<IAssetDescriptionStore>().SingleInstance();
        builder.RegisterType<AssetDescriptionVectorStore>().As<IAssetDescriptionVectorStore>().SingleInstance();
        builder.RegisterType<AssetDescriptionService>().As<IAssetDescriptionService>().SingleInstance();
        builder.RegisterType<AssetTextVectorizationService>().As<IAssetTextVectorizationService>().SingleInstance();
        builder.RegisterType<BackendLauncherService>().As<IBackendLauncher>().SingleInstance();
        return builder;
    }

    private static IConfiguration CreateConfiguration()
    {
        var baseDir = AppContext.BaseDirectory;
        var repositoryRoot = SharedDataPathHelper.GetRepositoryRoot();
        var backendDir = Path.Combine(repositoryRoot, "src", "backend");
        var appsettingsPath = Path.Combine(baseDir, "appsettings.json");

        var configuration = new ConfigurationBuilder()
            .SetBasePath(baseDir)
            .AddJsonFile(appsettingsPath, optional: true, reloadOnChange: false)
            .AddEnvironmentVariables(prefix: "ALS_")
            .Build();

        if (!string.IsNullOrWhiteSpace(configuration["BackendLauncher:BackendWorkingDirectory"]))
        {
            return configuration;
        }

        var fallbackValues = new[]
        {
            new KeyValuePair<string, string?>("BackendLauncher:BackendWorkingDirectory", backendDir),
        };

        return new ConfigurationBuilder()
            .AddConfiguration(configuration)
            .AddInMemoryCollection(fallbackValues)
            .Build();
    }
}
