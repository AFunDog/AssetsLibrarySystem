using System.IO;

namespace AssetsLibrarySystem.Avalonia.Services.AssetDescription;

public static class AssetDescriptionPathHelper
{
    public static string BuildDatabasePath()
    {
        var dataDirectory = Path.Combine(Directory.GetCurrentDirectory(), "data");
        Directory.CreateDirectory(dataDirectory);
        return Path.Combine(dataDirectory, "asset_descriptions.db");
    }
}
