using System;
using System.IO;

namespace AssetsLibrarySystem.Application.Infrastructure;

internal static class SharedDataPathHelper
{
    public static string GetDataFilePath(string fileName)
    {
        var dataDirectory = GetDataDirectory();
        var targetPath = Path.Combine(dataDirectory, fileName);
        if (File.Exists(targetPath))
        {
            return targetPath;
        }

        var legacyPath = TryFindLegacyDataFile(fileName);
        if (legacyPath is not null && !File.Exists(targetPath))
        {
            Directory.CreateDirectory(dataDirectory);
            File.Copy(legacyPath, targetPath, overwrite: false);
        }

        return targetPath;
    }

    public static string GetRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "src", "backend")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return AppContext.BaseDirectory;
    }

    private static string GetDataDirectory()
    {
        var dataDirectory = RuntimePathHelper.ResolveDataRoot();
        Directory.CreateDirectory(dataDirectory);
        return dataDirectory;
    }

    private static string? TryFindLegacyDataFile(string fileName)
    {
        var searchRoot = Path.Combine(GetRepositoryRoot(), "src", "avalonia");
        if (!Directory.Exists(searchRoot))
        {
            return null;
        }

        foreach (var candidate in Directory.EnumerateFiles(searchRoot, fileName, SearchOption.AllDirectories))
        {
            var normalized = candidate.Replace('\\', '/');
            if (normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase) &&
                normalized.Contains("/data/", StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return null;
    }
}
