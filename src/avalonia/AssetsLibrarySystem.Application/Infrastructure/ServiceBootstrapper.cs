using System;
using System.Collections.Generic;
using System.IO;
using Autofac;
using AssetsLibrarySystem.Avalonia.Services.AssetDescription;
using AssetsLibrarySystem.Avalonia.Services.AssetLibrary;
using AssetsLibrarySystem.Avalonia.Services.AssetSearch;
using AssetsLibrarySystem.Avalonia.Services.BackendLauncher;
using AssetsLibrarySystem.Avalonia.Services.BackgroundTasks;
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
        builder.RegisterType<AssetSearchService>().As<IAssetSearchService>().SingleInstance();
        builder.RegisterType<BackendLauncherService>().As<IBackendLauncher>().SingleInstance();
        builder.RegisterType<BackgroundTaskService>().As<IBackgroundTaskService>().SingleInstance();
        return builder;
    }

    private static IConfiguration CreateConfiguration()
    {
        var baseDir = AppContext.BaseDirectory;
        var appsettingsPath = Path.Combine(baseDir, "appsettings.json");
        var environmentName = ResolveEnvironmentName();
        var environmentAppsettingsPath = Path.Combine(baseDir, $"appsettings.{environmentName}.json");

        var configuration = new ConfigurationBuilder()
            .SetBasePath(baseDir)
            .AddJsonFile(appsettingsPath, optional: true, reloadOnChange: false)
            .AddJsonFile(environmentAppsettingsPath, optional: true, reloadOnChange: false)
            .AddEnvironmentVariables(prefix: "ALS_")
            .Build();

        var runtimeDataRoot = configuration["Runtime:DataRoot"];
        if (string.IsNullOrWhiteSpace(runtimeDataRoot))
        {
            runtimeDataRoot = RuntimePathHelper.ResolveDataRoot();
        }
        else
        {
            runtimeDataRoot = Path.GetFullPath(runtimeDataRoot);
        }

        var backendWorkingDirectory = configuration["BackendLauncher:BackendWorkingDirectory"];
        if (string.IsNullOrWhiteSpace(backendWorkingDirectory))
        {
            backendWorkingDirectory = RuntimePathHelper.ResolveBackendWorkingDirectory();
        }
        else
        {
            backendWorkingDirectory = Path.GetFullPath(backendWorkingDirectory);
        }

        var fallbackValues = new[]
        {
            new KeyValuePair<string, string?>("Runtime:DataRoot", runtimeDataRoot),
            new KeyValuePair<string, string?>("BackendLauncher:BackendWorkingDirectory", backendWorkingDirectory),
        };

        var finalConfiguration = new ConfigurationBuilder()
            .AddConfiguration(configuration)
            .AddInMemoryCollection(fallbackValues)
            .Build();

        RuntimePathHelper.ApplyEnvironmentOverrides(finalConfiguration["Runtime:DataRoot"] ?? runtimeDataRoot);
        return finalConfiguration;
    }

    private static string ResolveEnvironmentName()
    {
        var environmentName =
            Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");

        if (!string.IsNullOrWhiteSpace(environmentName))
        {
            return environmentName.Trim();
        }

#if DEBUG
        return "Development";
#else
        return "Production";
#endif
    }
}
