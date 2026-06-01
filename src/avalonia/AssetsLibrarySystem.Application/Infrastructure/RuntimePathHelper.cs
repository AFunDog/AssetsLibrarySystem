using System;
using System.IO;

namespace AssetsLibrarySystem.Avalonia.Infrastructure;

internal static class RuntimePathHelper
{
    private const string DataRootEnvironmentVariable = "DATA_ROOT";

    public static string ResolveDataRoot()
    {
        var configuredPath = Environment.GetEnvironmentVariable(DataRootEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return Path.GetFullPath(configuredPath);
        }

#if DEBUG
        return Path.GetFullPath(Path.Combine(SharedDataPathHelper.GetRepositoryRoot(), "data"));
#else
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "data"));
#endif
    }

    public static string ResolveBackendWorkingDirectory()
    {
#if DEBUG
        return Path.GetFullPath(Path.Combine(SharedDataPathHelper.GetRepositoryRoot(), "src", "backend"));
#else
        return AppContext.BaseDirectory;
#endif
    }

    public static void ApplyEnvironmentOverrides(string dataRoot)
    {
        if (!string.IsNullOrWhiteSpace(dataRoot))
        {
            Environment.SetEnvironmentVariable(DataRootEnvironmentVariable, Path.GetFullPath(dataRoot));
        }
    }
}
