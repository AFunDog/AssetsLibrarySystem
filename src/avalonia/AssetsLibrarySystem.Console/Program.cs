using System;
using System.IO;
using System.Threading.Tasks;
using AssetsLibrarySystem.Avalonia.Infrastructure;
using Autofac;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace AssetsLibrarySystem.Avalonia.ConsoleHost;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        ConfigureLogger();

        try
        {
            var builder = ServiceBootstrapper.CreateBuilder();
            var container = builder.Build();
            using var scope = container;

            var runner = new ConsoleCommandRunner(
                scope.Resolve<AssetsLibrarySystem.Avalonia.Services.AssetLibrary.IAssetLibraryService>(),
                scope.Resolve<AssetsLibrarySystem.Avalonia.Services.AssetDescription.IAssetDescriptionService>(),
                scope.Resolve<AssetsLibrarySystem.Avalonia.Services.AssetDescription.IAssetDescriptionStore>(),
                scope.Resolve<AssetsLibrarySystem.Avalonia.Services.AssetDescription.IAssetDescriptionVectorStore>(),
                scope.Resolve<AssetsLibrarySystem.Avalonia.Services.AssetDescription.IAssetTextVectorizationService>(),
                scope.Resolve<AssetsLibrarySystem.Avalonia.Services.BackendLauncher.IBackendLauncher>());

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
