using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AssetsLibrarySystem.Avalonia.Models;
using AssetsLibrarySystem.Avalonia.Services.AssetDescription;
using AssetsLibrarySystem.Avalonia.Services.AssetLibrary;
using AssetsLibrarySystem.Avalonia.Services.BackendLauncher;

namespace AssetsLibrarySystem.Avalonia.ConsoleHost;

public sealed class ConsoleCommandRunner
{
    private IAssetLibraryService LibraryService { get; }
    private IAssetDescriptionService DescriptionService { get; }
    private IAssetDescriptionStore DescriptionStore { get; }
    private IBackendLauncher BackendLauncher { get; }

    public ConsoleCommandRunner(
        IAssetLibraryService libraryService,
        IAssetDescriptionService descriptionService,
        IAssetDescriptionStore descriptionStore,
        IBackendLauncher backendLauncher)
    {
        LibraryService = libraryService;
        DescriptionService = descriptionService;
        DescriptionStore = descriptionStore;
        BackendLauncher = backendLauncher;
    }

    public async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            PrintHelp();
            return 0;
        }

        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "libraries" => await RunLibrariesAsync(args.Skip(1).ToArray()),
                "assets" => await RunAssetsAsync(args.Skip(1).ToArray()),
                _ => await RunLegacyShortcutAsync(args),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private async Task<int> RunLegacyShortcutAsync(string[] args)
    {
        if (args.Length == 0)
        {
            PrintHelp();
            return 0;
        }

        var command = args[0].ToLowerInvariant();
        if (command is "list-libraries" or "libs")
        {
            return await RunLibrariesAsync(new[] { "list" });
        }

        PrintHelp();
        return 1;
    }

    private async Task<int> RunLibrariesAsync(string[] args)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            PrintLibraryHelp();
            return 0;
        }

        return args[0].ToLowerInvariant() switch
        {
            "list" => await ListLibrariesAsync(),
            "add" => await AddLibraryAsync(args.Skip(1).ToArray()),
            "scan" => await ScanLibraryAsync(args.Skip(1).ToArray()),
            _ => PrintLibraryHelpAndFail()
        };
    }

    private async Task<int> RunAssetsAsync(string[] args)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            PrintAssetHelp();
            return 0;
        }

        return args[0].ToLowerInvariant() switch
        {
            "describe" => await DescribeAssetAsync(args.Skip(1).ToArray()),
            _ => PrintAssetHelpAndFail()
        };
    }

    private async Task<int> ListLibrariesAsync()
    {
        var libraries = await LibraryService.GetLibrariesAsync();
        if (libraries.Count == 0)
        {
            Console.WriteLine("当前没有登记的素材库。");
            return 0;
        }

        Console.WriteLine("素材库列表：");
        foreach (var library in libraries)
        {
            Console.WriteLine($"- {library.Id} | {library.Name} | {library.RootPath}");
        }

        return 0;
    }

    private async Task<int> AddLibraryAsync(string[] args)
    {
        var folderPath = args.FirstOrDefault(item => !item.StartsWith('-'))
            ?? GetOptionValue(args, "--path")
            ?? GetOptionValue(args, "-p");

        if (string.IsNullOrWhiteSpace(folderPath))
        {
            Console.Error.WriteLine("缺少素材库路径。");
            PrintLibraryHelp();
            return 1;
        }

        var library = await LibraryService.AddLibraryAsync(folderPath);
        Console.WriteLine($"已登记素材库：{library.Name}");
        Console.WriteLine($"- ID: {library.Id}");
        Console.WriteLine($"- Path: {library.RootPath}");
        return 0;
    }

    private async Task<int> ScanLibraryAsync(string[] args)
    {
        var libraryKey = args.FirstOrDefault(item => !item.StartsWith('-'))
            ?? GetOptionValue(args, "--library")
            ?? GetOptionValue(args, "-l");

        if (string.IsNullOrWhiteSpace(libraryKey))
        {
            Console.Error.WriteLine("缺少素材库标识。");
            PrintLibraryHelp();
            return 1;
        }

        var library = await ResolveLibraryAsync(libraryKey);
        if (library is null)
        {
            Console.Error.WriteLine($"未找到素材库：{libraryKey}");
            return 1;
        }

        var assets = await LibraryService.ScanLibraryAsync(library);
        Console.WriteLine($"素材库：{library.Name}");
        Console.WriteLine($"路径：{library.RootPath}");
        Console.WriteLine($"素材数量：{assets.Count}");

        foreach (var asset in assets)
        {
            var description = await DescriptionStore.TryGetAsync(asset.Id);
            var descriptionState = description is null ? "未描述" : $"已描述({description.Mode})";
            Console.WriteLine($"- {asset.RelativePath} | {asset.AssetType} | {asset.Stage} | {asset.AiState} | {descriptionState}");
        }

        return 0;
    }

    private async Task<int> DescribeAssetAsync(string[] args)
    {
        var libraryKey = GetOptionValue(args, "--library")
            ?? GetOptionValue(args, "-l");
        var assetKey = GetOptionValue(args, "--asset")
            ?? GetOptionValue(args, "-a");
        var prompt = GetOptionValue(args, "--prompt");
        var systemPrompt = GetOptionValue(args, "--system-prompt") ?? GetOptionValue(args, "--systemprompt");

        if (string.IsNullOrWhiteSpace(libraryKey) || string.IsNullOrWhiteSpace(assetKey))
        {
            Console.Error.WriteLine("需要同时提供 --library 和 --asset。");
            PrintAssetHelp();
            return 1;
        }

        var library = await ResolveLibraryAsync(libraryKey);
        if (library is null)
        {
            Console.Error.WriteLine($"未找到素材库：{libraryKey}");
            return 1;
        }

        var assets = await LibraryService.ScanLibraryAsync(library);
        var asset = ResolveAsset(assets, assetKey);
        if (asset is null)
        {
            Console.Error.WriteLine($"未找到素材：{assetKey}");
            return 1;
        }

        await BackendLauncher.StartAsync();
        try
        {
            var document = await DescriptionService.DescribeAsync(
                asset,
                BackendLauncher.BaseUrl,
                prompt,
                systemPrompt);

            Console.WriteLine("描述生成完成。");
            Console.WriteLine($"- 素材: {document.AssetName}");
            Console.WriteLine($"- 存储: {document.StorePath}");
            Console.WriteLine($"- 模式: {document.Mode}");
            Console.WriteLine($"- 时间: {document.GeneratedAt:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine($"- 文本: {document.Description}");
        }
        finally
        {
            await BackendLauncher.StopAsync();
        }

        return 0;
    }

    private async Task<LibraryWorkspace?> ResolveLibraryAsync(string key)
    {
        var libraries = await LibraryService.GetLibrariesAsync();
        return libraries.FirstOrDefault(library =>
            string.Equals(library.Id, key, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(library.Name, key, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(library.RootPath, key, StringComparison.OrdinalIgnoreCase));
    }

    private static ManagedAssetRecord? ResolveAsset(IEnumerable<ManagedAssetRecord> assets, string key)
    {
        return assets.FirstOrDefault(asset =>
            string.Equals(asset.Id, key, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(asset.RelativePath, key, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(asset.LocalPath, key, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(asset.Name, key, StringComparison.OrdinalIgnoreCase));
    }

    private static string? GetOptionValue(string[] args, string name)
    {
        for (var index = 0; index < args.Length; index++)
        {
            if (!string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return index + 1 < args.Length ? args[index + 1] : null;
        }

        return null;
    }

    private static bool IsHelp(string arg)
    {
        return string.Equals(arg, "--help", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(arg, "-h", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(arg, "help", StringComparison.OrdinalIgnoreCase);
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            用法:
              libraries list
              libraries add <folderPath>
              libraries scan <libraryId|libraryName|rootPath>
              assets describe --library <libraryId|libraryName|rootPath> --asset <assetId|relativePath|fileName> [--prompt <prompt>] [--system-prompt <prompt>]

            示例:
              libraries add D:\Data\WebGal
              libraries scan 我的素材库
              assets describe --library 我的素材库 --asset background.png
            """);
    }

    private static void PrintLibraryHelp()
    {
        Console.WriteLine("""
            libraries 命令:
              libraries list
              libraries add <folderPath>
              libraries scan <libraryId|libraryName|rootPath>
            """);
    }

    private static void PrintAssetHelp()
    {
        Console.WriteLine("""
            assets 命令:
              assets describe --library <libraryId|libraryName|rootPath> --asset <assetId|relativePath|fileName> [--prompt <prompt>] [--system-prompt <prompt>]
            """);
    }

    private static int PrintLibraryHelpAndFail()
    {
        PrintLibraryHelp();
        return 1;
    }

    private static int PrintAssetHelpAndFail()
    {
        PrintAssetHelp();
        return 1;
    }
}
