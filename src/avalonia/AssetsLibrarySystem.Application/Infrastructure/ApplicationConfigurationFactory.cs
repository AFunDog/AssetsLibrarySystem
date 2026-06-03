using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Configuration;

namespace AssetsLibrarySystem.Application.Infrastructure;

public static class ApplicationConfigurationFactory
{
    public static IConfiguration CreateConfiguration()
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
