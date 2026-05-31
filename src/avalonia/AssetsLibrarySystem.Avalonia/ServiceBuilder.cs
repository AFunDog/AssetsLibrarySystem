using System;
using System.IO;
using System.Reflection;
using Autofac;
using AssetsLibrarySystem.Avalonia.Services.BackendLauncher;
using AssetsLibrarySystem.Avalonia.ViewModels;
using Autofac.Core.Lifetime;

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

        // 显式注册 BackendLauncherModule
        var backendOptions = CreateBackendLauncherOptions();
        builder.RegisterModule(new BackendLauncherModule(backendOptions));

        // 自动扫描当前程序集中所有无参 Autofac Module 并注册
        var assembly = Assembly.GetExecutingAssembly();
        builder.RegisterAssemblyModules(assembly);

        // 注册 ViewModels（自动注入构造函数参数）
        builder.RegisterType<MainWindowViewModel>().AsSelf().InstancePerDependency();

        var container = builder.Build();
        return container;
    }
    
    /// <summary>
    /// 根据当前可执行文件位置推算出 src/backend 的绝对路径。
    /// </summary>
    private static BackendLauncherOptions CreateBackendLauncherOptions()
    {
        var baseDir = AppContext.BaseDirectory;
        // 从 bin/Debug/net10.0 回退四层到项目根，再拼 src/backend
        var backendDir = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "src", "backend"));

        return new BackendLauncherOptions
        {
            BackendWorkingDirectory = backendDir,
        };
    }
}