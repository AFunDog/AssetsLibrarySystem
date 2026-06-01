using System;
using System.Collections.Generic;
using System.IO;
using Autofac;
using AssetsLibrarySystem.Avalonia.Services.AssetDescription;
using AssetsLibrarySystem.Avalonia.Services.AssetLibrary;
using AssetsLibrarySystem.Avalonia.Services.BackendLauncher;
using AssetsLibrarySystem.Avalonia.ViewModels;
using Autofac.Core.Lifetime;
using Microsoft.Extensions.Configuration;

namespace AssetsLibrarySystem.Avalonia;

/// <summary>
/// 应用服务注册中心：构建 Autofac 容器，暴露服务解析能力。
/// </summary>
public static class ServiceBuilder
{
    private static Lazy<ILifetimeScope> Instance { get; } = new(Init);
    
    /// <summary>
    /// 当前容器上下文，用于手动解析服务。
    /// </summary>
    public static ILifetimeScope Services => Instance.Value;

    /// <summary>
    /// 初始化并构建容器，在此处注册所有模块。
    /// </summary>
    public static ILifetimeScope Init()
    {
        var builder = new ContainerBuilder();

        builder.RegisterInstance(CreateConfiguration()).As<IConfiguration>().SingleInstance();
        builder.RegisterModule<BackendLauncherModule>();
        builder.RegisterType<AssetLibraryService>().As<IAssetLibraryService>().SingleInstance();
        builder.RegisterType<AssetDescriptionStore>().As<IAssetDescriptionStore>().SingleInstance();
        builder.RegisterType<AssetDescriptionService>().As<IAssetDescriptionService>().SingleInstance();

        // 注册 ViewModels（自动注入构造函数参数）
        builder.RegisterType<MainWindowViewModel>().AsSelf().InstancePerDependency();

        var container = builder.Build();
        return container;
    }

    /// <summary>
    /// 构建桌面端配置，并为未显式指定的后端工作目录补默认值。
    /// 这样开发态和发布态都能用同一套配置入口。
    /// </summary>
    private static IConfiguration CreateConfiguration()
    {
        var baseDir = AppContext.BaseDirectory;
        var backendDir = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", "..", "src", "backend"));
        var appsettingsPath = Path.Combine(baseDir, "appsettings.json");

        var configuration = new ConfigurationBuilder()
            .SetBasePath(baseDir)
            .AddJsonFile(appsettingsPath, optional: true, reloadOnChange: false)
            .AddEnvironmentVariables(prefix: "ALS_")
            .Build();

        var configuredWorkingDirectory = configuration["BackendLauncher:BackendWorkingDirectory"];
        if (!string.IsNullOrWhiteSpace(configuredWorkingDirectory))
        {
            return configuration;
        }

        // 未显式配置时，回退到仓库内的 Python 后端目录。
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
