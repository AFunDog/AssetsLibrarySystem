using AssetsLibrarySystem.Application.Infrastructure;
using Autofac;

namespace AssetsLibrarySystem.ConsoleHost.DependencyInjection;

public sealed class ConsoleHostModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<ConfigurationSearchModelOptionsProvider>()
            .As<ISearchModelOptionsProvider>()
            .SingleInstance();

        builder.RegisterType<ConsoleCommandRunner>()
            .AsSelf()
            .SingleInstance();
    }
}
