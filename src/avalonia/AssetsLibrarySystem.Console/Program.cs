using System;
using System.IO;
using System.Threading.Tasks;
using AssetsLibrarySystem.Application.DependencyInjection;
using AssetsLibrarySystem.ConsoleHost.DependencyInjection;
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
            builder.RegisterModule<ApplicationModule>();
            builder.RegisterModule<ConsoleHostModule>();

            var container = builder.Build();
            using var scope = container;
            var runner = scope.Resolve<ConsoleCommandRunner>();

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
