using Autofac;

namespace AssetsLibrarySystem.Avalonia.Services.BackendLauncher;

/// <summary>
/// AutoFac 模块：注册 BackendLauncher 相关服务。
/// </summary>
public sealed class BackendLauncherModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<BackendLauncherService>()
            .As<IBackendLauncher>()
            .SingleInstance()
            .AutoActivate(); // 容器构建时立即实例化，后续可在 App 启动时调用 StartAsync
    }
}
