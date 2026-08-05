using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AssetsLibrarySystem.Application.Models;
using AssetsLibrarySystem.Application.Services.AssetDescription;
using AssetsLibrarySystem.Application.Services.AssetLibrary;
using AssetsLibrarySystem.Application.Services.AssetSearch;
using AssetsLibrarySystem.Application.Services.BackendLauncher;
using AssetsLibrarySystem.Application.UseCases.AssetOperations;

namespace AssetsLibrarySystem.ConsoleHost;

public sealed class ConsoleCommandRunner
{
    private IAssetLibraryService LibraryService { get; }
    private IAssetDescriptionStore DescriptionStore { get; }
    private IAssetDescriptionVectorStore VectorStore { get; }
    private IAssetSearchService AssetSearchService { get; }
    private IBackendLauncher BackendLauncher { get; }
    private DescribeAssetsUseCase DescribeAssetsUseCase { get; }
    private VectorizeDescriptionsUseCase VectorizeDescriptionsUseCase { get; }
    private RebuildSearchIndexUseCase RebuildSearchIndexUseCase { get; }
    private SplitClipSegmentsUseCase SplitClipSegmentsUseCase { get; }
    private DeleteAssetDescriptionUseCase DeleteAssetDescriptionUseCase { get; }

    public ConsoleCommandRunner(
        IAssetLibraryService libraryService,
        IAssetDescriptionStore descriptionStore,
        IAssetDescriptionVectorStore vectorStore,
        IAssetSearchService assetSearchService,
        IBackendLauncher backendLauncher,
        DescribeAssetsUseCase describeAssetsUseCase,
        VectorizeDescriptionsUseCase vectorizeDescriptionsUseCase,
        RebuildSearchIndexUseCase rebuildSearchIndexUseCase,
        SplitClipSegmentsUseCase splitClipSegmentsUseCase,
        DeleteAssetDescriptionUseCase deleteAssetDescriptionUseCase)
    {
        LibraryService = libraryService;
        DescriptionStore = descriptionStore;
        VectorStore = vectorStore;
        AssetSearchService = assetSearchService;
        BackendLauncher = backendLauncher;
        DescribeAssetsUseCase = describeAssetsUseCase;
        VectorizeDescriptionsUseCase = vectorizeDescriptionsUseCase;
        RebuildSearchIndexUseCase = rebuildSearchIndexUseCase;
        SplitClipSegmentsUseCase = splitClipSegmentsUseCase;
        DeleteAssetDescriptionUseCase = deleteAssetDescriptionUseCase;
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
            "remove" => await RemoveLibraryAsync(args.Skip(1).ToArray()),
            "rename" => await RenameLibraryAsync(args.Skip(1).ToArray()),
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
            "describe-dir" => await DescribeDirectoryAsync(args.Skip(1).ToArray()),
            "split" => await SplitClipAssetAsync(args.Skip(1).ToArray()),
            "vectorize-missing" => await VectorizeMissingDescriptionsAsync(args.Skip(1).ToArray()),
            "search" => await SearchAssetsAsync(args.Skip(1).ToArray()),
            "query" => await SearchAssetsAsync(args.Skip(1).ToArray()),
            "reindex" => await ReindexSearchIndexAsync(args.Skip(1).ToArray()),
            "delete" => await DeleteAssetAsync(args.Skip(1).ToArray()),
            "rename" => await RenameAssetAsync(args.Skip(1).ToArray()),
            "set-type" => await SetAssetTypeAsync(args.Skip(1).ToArray()),
            "tag" => await TagAssetAsync(args.Skip(1).ToArray()),
            "set-description" => await SetAssetDescriptionAsync(args.Skip(1).ToArray()),
            "clear-description" => await ClearAssetDescriptionAsync(args.Skip(1).ToArray()),
            "usage" => await ShowTokenUsageAsync(args.Skip(1).ToArray()),
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

        var kindValue = GetOptionValue(args, "--kind") ?? GetOptionValue(args, "-k");
        var kind = LibraryKind.Standard;
        if (!string.IsNullOrWhiteSpace(kindValue))
        {
            if (string.Equals(kindValue, "clip", StringComparison.OrdinalIgnoreCase))
            {
                kind = LibraryKind.Clip;
            }
            else if (!string.Equals(kindValue, "standard", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine($"未知素材库类型：{kindValue}（支持 standard / clip）。");
                return 1;
            }
        }

        var library = await LibraryService.AddLibraryAsync(folderPath, kind);
        Console.WriteLine($"已登记素材库：{library.Name}（{(kind == LibraryKind.Clip ? "剪辑库" : "标准库")}）");
        Console.WriteLine($"- ID: {library.Id}");
        Console.WriteLine($"- Path: {library.RootPath}");
        return 0;
    }

    private async Task<int> RemoveLibraryAsync(string[] args)
    {
        var targetKey = args.FirstOrDefault(item => !item.StartsWith('-'))
            ?? GetOptionValue(args, "--library")
            ?? GetOptionValue(args, "-l");

        if (string.IsNullOrWhiteSpace(targetKey))
        {
            Console.Error.WriteLine("缺少素材库标识。");
            PrintLibraryHelp();
            return 1;
        }

        var library = await ResolveLibraryAsync(targetKey);
        if (library is null)
        {
            Console.Error.WriteLine($"未找到素材库：{targetKey}");
            return 1;
        }

        await LibraryService.DeleteLibraryAsync(library.Id);
        Console.WriteLine($"已删除素材库：{library.Name}（含全部素材、描述与向量数据）");
        return 0;
    }

    private async Task<int> RenameLibraryAsync(string[] args)
    {
        var targetKey = args.FirstOrDefault(item => !item.StartsWith('-'))
            ?? GetOptionValue(args, "--library")
            ?? GetOptionValue(args, "-l");
        var newName = GetOptionValue(args, "--name");

        if (string.IsNullOrWhiteSpace(targetKey))
        {
            Console.Error.WriteLine("缺少素材库标识。");
            PrintLibraryHelp();
            return 1;
        }

        if (string.IsNullOrWhiteSpace(newName))
        {
            Console.Error.WriteLine("缺少新名称：--name <新名称>。");
            return 1;
        }

        var library = await ResolveLibraryAsync(targetKey);
        if (library is null)
        {
            Console.Error.WriteLine($"未找到素材库：{targetKey}");
            return 1;
        }

        await LibraryService.UpdateLibraryAsync(library.Id, newName.Trim());
        Console.WriteLine($"已重命名素材库：{library.Name} -> {newName.Trim()}");
        return 0;
    }

    private async Task<int> ScanLibraryAsync(string[] args)
    {
        var targetPath = args.FirstOrDefault(item => !item.StartsWith('-'))
            ?? GetOptionValue(args, "--library")
            ?? GetOptionValue(args, "-l");

        if (string.IsNullOrWhiteSpace(targetPath))
        {
            Console.Error.WriteLine("缺少扫描目标。");
            PrintLibraryHelp();
            return 1;
        }

        var library = await ResolveLibraryAsync(targetPath);
        if (library is not null)
        {
            var assets = await LibraryService.ScanLibraryAsync(library);
            return await PrintScanResultAsync($"{library.Name} ({library.RootPath})", assets);
        }

        if (Directory.Exists(targetPath))
        {
            var registeredLibrary = await LibraryService.AddLibraryAsync(targetPath);
            var assets = await LibraryService.ScanLibraryAsync(registeredLibrary);
            return await PrintScanResultAsync(targetPath, assets);
        }

        if (File.Exists(targetPath))
        {
            Console.Error.WriteLine("libraries scan 只接受目录路径或已登记素材库，不接受单个文件。");
            return 1;
        }

        Console.Error.WriteLine($"未找到扫描目标：{targetPath}");
        return 1;
    }

    private async Task<int> DescribeAssetAsync(string[] args)
    {
        var libraryKey = GetOptionValue(args, "--library")
            ?? GetOptionValue(args, "-l");
        var assetKey = GetOptionValue(args, "--asset")
            ?? GetOptionValue(args, "-a");
        var prompt = GetOptionValue(args, "--prompt");
        var systemPrompt = GetOptionValue(args, "--system-prompt") ?? GetOptionValue(args, "--systemprompt");
        var rangeStart = TryParseSeconds(GetOptionValue(args, "--start"));
        var rangeEnd = TryParseSeconds(GetOptionValue(args, "--end"));

        if (string.IsNullOrWhiteSpace(libraryKey) || string.IsNullOrWhiteSpace(assetKey))
        {
            Console.Error.WriteLine("需要同时提供 --library 和 --asset。");
            PrintAssetHelp();
            return 1;
        }

        if (rangeStart is not null && rangeEnd is not null && rangeEnd <= rangeStart)
        {
            Console.Error.WriteLine("时间范围无效：--end 必须大于 --start。");
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
            var document = await DescribeSingleAssetAsync(asset, prompt, systemPrompt, rangeStart, rangeEnd);
            PrintDescriptionResult(document);
        }
        finally
        {
            await BackendLauncher.StopAsync();
        }

        return 0;
    }

    private async Task<int> SplitClipAssetAsync(string[] args)
    {
        var libraryKey = GetOptionValue(args, "--library")
            ?? GetOptionValue(args, "-l");
        var assetKey = GetOptionValue(args, "--asset")
            ?? GetOptionValue(args, "-a");
        var rangeStart = TryParseSeconds(GetOptionValue(args, "--start"));
        var rangeEnd = TryParseSeconds(GetOptionValue(args, "--end"));

        if (string.IsNullOrWhiteSpace(libraryKey) || string.IsNullOrWhiteSpace(assetKey))
        {
            Console.Error.WriteLine("需要同时提供 --library 和 --asset。");
            PrintAssetHelp();
            return 1;
        }

        if (rangeStart is not null && rangeEnd is not null && rangeEnd <= rangeStart)
        {
            Console.Error.WriteLine("时间范围无效：--end 必须大于 --start。");
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

        if (asset.AssetType != "视频剪辑")
        {
            Console.Error.WriteLine($"仅视频剪辑素材支持场景分割，当前类型：{asset.AssetType}");
            return 1;
        }

        await BackendLauncher.StartAsync();
        try
        {
            var result = await SplitClipSegmentsUseCase.ExecuteAsync(
                [asset],
                BackendLauncher.BaseUrl,
                rangeStart,
                rangeEnd);

            if (result.SuccessCount > 0)
            {
                Console.WriteLine($"分割完成：{asset.RelativePath}");
            }
            else if (result.SkipCount > 0)
            {
                Console.WriteLine($"已存在分割结果，跳过：{asset.RelativePath}");
            }
            else
            {
                Console.Error.WriteLine($"分割失败：{asset.RelativePath}");
                return 1;
            }
        }
        finally
        {
            await BackendLauncher.StopAsync();
        }

        return 0;
    }

    private static double? TryParseSeconds(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        // 非数字输入直接报错，避免静默降级为“全片描述”造成意外费用
        if (!double.TryParse(text.Trim(), out var seconds))
        {
            throw new ArgumentException($"时间参数不是有效数字: {text}（--start/--end 使用秒数，如 --start 12.5）");
        }

        return seconds;
    }

    private async Task<int> DescribeDirectoryAsync(string[] args)
    {
        var libraryKey = GetOptionValue(args, "--library")
            ?? GetOptionValue(args, "-l")
            ?? args.FirstOrDefault(item => !item.StartsWith('-'));
        var folderKey = GetOptionValue(args, "--folder")
            ?? GetOptionValue(args, "-f");
        var prompt = GetOptionValue(args, "--prompt");
        var systemPrompt = GetOptionValue(args, "--system-prompt") ?? GetOptionValue(args, "--systemprompt");

        if (string.IsNullOrWhiteSpace(libraryKey) || string.IsNullOrWhiteSpace(folderKey))
        {
            Console.Error.WriteLine("需要同时提供 --library 和 --folder。");
            PrintAssetHelp();
            return 1;
        }

        var library = await ResolveLibraryAsync(libraryKey);
        if (library is null)
        {
            Console.Error.WriteLine($"未找到素材库：{libraryKey}");
            return 1;
        }

        var folderPath = Path.IsPathRooted(folderKey)
            ? Path.GetFullPath(folderKey)
            : Path.GetFullPath(Path.Combine(library.RootPath, folderKey));

        if (!Directory.Exists(folderPath))
        {
            Console.Error.WriteLine($"未找到文件夹：{folderKey}");
            return 1;
        }

        if (!IsSubPathOf(folderPath, library.RootPath))
        {
            Console.Error.WriteLine("指定文件夹必须位于该素材库目录内。");
            return 1;
        }

        var folderRelativePath = Path.GetRelativePath(library.RootPath, folderPath);
        if (string.Equals(folderRelativePath, ".", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("请指定素材库中的子文件夹，不要直接指定素材库根目录。");
            return 1;
        }

        var assets = await LibraryService.ScanLibraryAsync(library);
        var targetAssets = assets
            .Where(asset => IsAssetUnderFolder(library.RootPath, folderPath, asset.RelativePath))
            .ToList();
        var targetLabel = $"{library.Name} / {folderRelativePath}";

        if (targetAssets.Count == 0)
        {
            Console.WriteLine($"描述目标：{targetLabel}");
            Console.WriteLine("该文件夹下没有可描述的素材。");
            return 0;
        }

        Console.WriteLine($"描述目标：{targetLabel}");
        Console.WriteLine($"素材数量：{targetAssets.Count}");

        await BackendLauncher.StartAsync();
        try
        {
            var currentIndex = 0;
            var result = await DescribeAssetsUseCase.ExecuteAsync(
                targetAssets,
                BackendLauncher.BaseUrl,
                prompt,
                systemPrompt,
                progress: progress =>
                {
                    if (progress.Kind == DescribeAssetProgressKind.Queued)
                    {
                        currentIndex++;
                        Console.WriteLine($"[{currentIndex}/{targetAssets.Count}] 开始描述：{progress.Asset.RelativePath}");
                    }
                    else if (progress.Kind == DescribeAssetProgressKind.Completed && progress.Document is not null)
                    {
                        Console.WriteLine($"[{currentIndex}/{targetAssets.Count}] 完成：{progress.Asset.RelativePath}");
                        PrintDescriptionResult(progress.Document);
                    }
                    else if (progress.Kind == DescribeAssetProgressKind.Failed && progress.Error is not null)
                    {
                        Console.Error.WriteLine($"[{currentIndex}/{targetAssets.Count}] 失败：{progress.Asset.RelativePath} | {progress.Error.Message}");
                    }

                    return Task.CompletedTask;
                });

            Console.WriteLine($"批量描述结束：成功 {result.SuccessCount}，失败 {result.FailureCount}");
            return result.FailureCount == 0 ? 0 : 1;
        }
        finally
        {
            await BackendLauncher.StopAsync();
        }
    }

    private async Task<int> VectorizeMissingDescriptionsAsync(string[] args)
    {
        var libraryKey = GetOptionValue(args, "--library")
            ?? GetOptionValue(args, "-l");
        var libraries = string.IsNullOrWhiteSpace(libraryKey)
            ? await LibraryService.GetLibrariesAsync()
            : new List<LibraryWorkspace?>
            {
                await ResolveLibraryAsync(libraryKey)
            }.Where(library => library is not null).Cast<LibraryWorkspace>().ToList();

        if (libraries.Count == 0)
        {
            if (!string.IsNullOrWhiteSpace(libraryKey))
            {
                Console.Error.WriteLine($"未找到素材库：{libraryKey}");
            }
            else
            {
                Console.Error.WriteLine("当前没有登记的素材库。");
            }

            return 1;
        }

        var pending = new List<(LibraryWorkspace Library, ManagedAssetRecord Asset, AssetDescriptionDocument Description)>();

        foreach (var library in libraries)
        {
            var assets = await LibraryService.ScanLibraryAsync(library);
            foreach (var asset in assets)
            {
                var description = await DescriptionStore.TryGetAsync(asset.DatabaseId);
                if (description is null)
                {
                    continue;
                }

                pending.Add((library, asset, description));
            }
        }

        Console.WriteLine($"素材库数量：{libraries.Count}");
        Console.WriteLine($"可向量化描述：{pending.Count}");

        if (pending.Count == 0)
        {
            Console.WriteLine("没有找到未向量化的描述数据。");
            return 0;
        }

        var libraryByAssetId = pending.ToDictionary(item => item.Asset.Id, item => item.Library);
        var currentIndex = 0;

        await BackendLauncher.StartAsync();
        try
        {
            var result = await VectorizeDescriptionsUseCase.ExecuteAsync(
                pending.Select(item => item.Asset).ToList(),
                BackendLauncher.BaseUrl,
                progress =>
                {
                    if (progress.Kind == VectorizeDescriptionProgressKind.Completed)
                    {
                        currentIndex++;
                        var library = libraryByAssetId[progress.Asset.Id];
                        Console.WriteLine($"[{currentIndex}/{pending.Count}] 完成：{library.Name} / {progress.Asset.RelativePath}");
                    }
                    else if (progress.Kind == VectorizeDescriptionProgressKind.Skipped)
                    {
                        currentIndex++;
                        var library = libraryByAssetId[progress.Asset.Id];
                        Console.WriteLine($"[{currentIndex}/{pending.Count}] 跳过：{library.Name} / {progress.Asset.RelativePath} | {progress.SkipReason}");
                    }
                    else if (progress.Kind == VectorizeDescriptionProgressKind.Failed && progress.Error is not null)
                    {
                        currentIndex++;
                        var library = libraryByAssetId[progress.Asset.Id];
                        Console.Error.WriteLine($"[{currentIndex}/{pending.Count}] 失败：{library.Name} / {progress.Asset.RelativePath} | {progress.Error.Message}");
                    }

                    return Task.CompletedTask;
                });

            Console.WriteLine($"向量化结束：成功 {result.SuccessCount}，跳过 {result.SkipCount}，失败 {result.FailureCount}");
            return result.FailureCount == 0 ? 0 : 1;
        }
        finally
        {
            await BackendLauncher.StopAsync();
        }
    }

    private async Task<int> SearchAssetsAsync(string[] args)
    {
        var query = GetOptionValue(args, "--query")
            ?? GetOptionValue(args, "-q")
            ?? GetLeadingText(args);
        var candidateTopK = GetIntOptionValue(args, "--candidate-top-k")
            ?? GetIntOptionValue(args, "--candidate-topk")
            ?? 20;
        var finalTopK = GetIntOptionValue(args, "--top-k")
            ?? GetIntOptionValue(args, "--topk")
            ?? 5;
        var assetFormat = GetOptionValue(args, "--format")
            ?? GetOptionValue(args, "--asset-format");

        if (string.IsNullOrWhiteSpace(query))
        {
            Console.Error.WriteLine("需要提供查询文本。");
            PrintAssetHelp();
            return 1;
        }

        await BackendLauncher.StartAsync();
        try
        {
            var response = await AssetSearchService.SearchAsync(
                BackendLauncher.BaseUrl,
                query,
                candidateTopK,
                finalTopK,
                assetFormat);
            PrintSearchResult(response);
        }
        finally
        {
            await BackendLauncher.StopAsync();
        }

        return 0;
    }

    private async Task<int> DeleteAssetAsync(string[] args)
    {
        var (library, asset) = await ResolveLibraryAndAssetAsync(args);
        if (library is null || asset is null)
        {
            return 1;
        }

        await LibraryService.DeleteAssetAsync(asset.DatabaseId);
        Console.WriteLine($"已删除素材：{asset.RelativePath}（含描述与向量数据）");
        return 0;
    }

    private async Task<int> RenameAssetAsync(string[] args)
    {
        var (library, asset) = await ResolveLibraryAndAssetAsync(args);
        if (library is null || asset is null)
        {
            return 1;
        }

        var newName = GetOptionValue(args, "--name");
        if (string.IsNullOrWhiteSpace(newName))
        {
            Console.Error.WriteLine("缺少新名称：--name <新名称>。");
            return 1;
        }

        await LibraryService.UpdateAssetNameAsync(asset.DatabaseId, newName.Trim());
        Console.WriteLine($"已重命名素材：{asset.RelativePath} -> {newName.Trim()}");
        return 0;
    }

    private async Task<int> SetAssetTypeAsync(string[] args)
    {
        var (library, asset) = await ResolveLibraryAndAssetAsync(args);
        if (library is null || asset is null)
        {
            return 1;
        }

        var newType = GetOptionValue(args, "--type") ?? GetOptionValue(args, "-t");
        if (string.IsNullOrWhiteSpace(newType))
        {
            Console.Error.WriteLine("缺少目标类型：--type <视频|视频剪辑>。");
            return 1;
        }

        if (!string.Equals(newType, "视频", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(newType, "视频剪辑", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine($"仅支持 视频 ↔ 视频剪辑 互转，收到：{newType}");
            return 1;
        }

        var normalizedType = string.Equals(newType, "视频剪辑", StringComparison.OrdinalIgnoreCase) ? "视频剪辑" : "视频";
        await LibraryService.UpdateAssetTypeAsync(asset.DatabaseId, normalizedType);
        Console.WriteLine($"已更改素材类型：{asset.RelativePath} -> {normalizedType}");
        Console.WriteLine("旧描述已标记过期并删除旧向量，请重新生成描述。");
        return 0;
    }

    private async Task<int> TagAssetAsync(string[] args)
    {
        var (library, asset) = await ResolveLibraryAndAssetAsync(args);
        if (library is null || asset is null)
        {
            return 1;
        }

        var setText = GetOptionValue(args, "--set");
        var addText = GetOptionValue(args, "--add");
        var removeText = GetOptionValue(args, "--remove");

        if (string.IsNullOrWhiteSpace(setText) && string.IsNullOrWhiteSpace(addText) && string.IsNullOrWhiteSpace(removeText))
        {
            Console.Error.WriteLine("需要提供 --set <tag1,tag2> / --add <tag> / --remove <tag> 之一。");
            return 1;
        }

        var tags = asset.Tags.ToList();
        if (!string.IsNullOrWhiteSpace(setText))
        {
            tags = SplitTags(setText);
        }

        foreach (var tag in SplitTags(addText))
        {
            if (!tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
            {
                tags.Add(tag);
            }
        }

        foreach (var tag in SplitTags(removeText))
        {
            tags.RemoveAll(item => string.Equals(item, tag, StringComparison.OrdinalIgnoreCase));
        }

        await LibraryService.UpdateAssetTagsAsync(asset.DatabaseId, tags.ToArray());
        Console.WriteLine($"已更新标签：{asset.RelativePath} -> [{string.Join(", ", tags)}]");
        return 0;
    }

    private async Task<int> SetAssetDescriptionAsync(string[] args)
    {
        var (library, asset) = await ResolveLibraryAndAssetAsync(args);
        if (library is null || asset is null)
        {
            return 1;
        }

        var text = GetOptionValue(args, "--text");
        var filePath = GetOptionValue(args, "--file");

        if (string.IsNullOrWhiteSpace(text) && string.IsNullOrWhiteSpace(filePath))
        {
            Console.Error.WriteLine("需要提供 --text <内容> 或 --file <路径>。");
            return 1;
        }

        if (!string.IsNullOrWhiteSpace(text) && !string.IsNullOrWhiteSpace(filePath))
        {
            Console.Error.WriteLine("--text 与 --file 只能提供一个。");
            return 1;
        }

        if (!string.IsNullOrWhiteSpace(filePath))
        {
            if (!File.Exists(filePath))
            {
                Console.Error.WriteLine($"未找到描述文本文件：{filePath}");
                return 1;
            }

            text = await File.ReadAllTextAsync(filePath);
        }

        var existing = await DescriptionStore.TryGetAsync(asset.DatabaseId);
        if (existing is not null)
        {
            await DescriptionStore.UpdateDescriptionAsync(asset.DatabaseId, text!.Trim());
        }
        else
        {
            // 素材尚无描述记录：构造手工文档写入（模式标记为「手动」）
            var document = new AssetDescriptionDocument(
                AssetId: asset.DatabaseId,
                AssetUid: asset.Id,
                AssetName: asset.Name,
                AssetType: asset.AssetType,
                CurrentPath: asset.LocalPath,
                Description: text!.Trim(),
                BackendEndpoint: "manual",
                Mode: "手动",
                GeneratedAt: DateTimeOffset.Now,
                TokenUsage: null,
                Prompt: null,
                SystemPrompt: null,
                ContentHash: null,
                MetadataStatus: "ok");
            await DescriptionStore.SaveAsync(document);
        }

        Console.WriteLine($"已保存描述文本：{asset.RelativePath}");
        return 0;
    }

    private async Task<int> ClearAssetDescriptionAsync(string[] args)
    {
        var (library, asset) = await ResolveLibraryAndAssetAsync(args);
        if (library is null || asset is null)
        {
            return 1;
        }

        var result = await DeleteAssetDescriptionUseCase.ExecuteAsync(asset);
        Console.WriteLine(result.DescriptionDeleted || result.VectorDeleted
            ? $"已清除描述与向量：{asset.RelativePath}（本地索引已重建）"
            : $"该素材没有描述或向量记录：{asset.RelativePath}");
        return 0;
    }

    private async Task<(LibraryWorkspace? Library, ManagedAssetRecord? Asset)> ResolveLibraryAndAssetAsync(
        string[] args)
    {
        var libraryKey = GetOptionValue(args, "--library") ?? GetOptionValue(args, "-l");
        var assetKey = GetOptionValue(args, "--asset") ?? GetOptionValue(args, "-a");

        if (string.IsNullOrWhiteSpace(libraryKey) || string.IsNullOrWhiteSpace(assetKey))
        {
            Console.Error.WriteLine($"需要同时提供 --library 和 --asset。");
            PrintAssetHelp();
            return (null, null);
        }

        var library = await ResolveLibraryAsync(libraryKey);
        if (library is null)
        {
            Console.Error.WriteLine($"未找到素材库：{libraryKey}");
            return (null, null);
        }

        var assets = await LibraryService.ScanLibraryAsync(library);
        var asset = ResolveAsset(assets, assetKey);
        if (asset is null)
        {
            Console.Error.WriteLine($"未找到素材：{assetKey}");
            return (null, null);
        }

        return (library, asset);
    }

    private static List<string> SplitTags(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        return text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<int> ReindexSearchIndexAsync(string[] args)
    {
        if (args.Length > 0 && IsHelp(args[0]))
        {
            PrintAssetHelp();
            return 0;
        }

        var response = await RebuildSearchIndexUseCase.ExecuteAsync();
        Console.WriteLine("本地向量索引重建完成。");
        Console.WriteLine($"- 素材描述数: {response.DocumentCount}");
        Console.WriteLine($"- 向量维度: {response.VectorDim}");
        Console.WriteLine($"- 数据库: {response.DatabasePath}");
        Console.WriteLine($"- 索引: {response.IndexPath}");
        Console.WriteLine($"- 元数据: {response.MetadataPath}");
        Console.WriteLine($"- 向量模型: {string.Join(", ", response.EmbeddingModels)}");

        return 0;
    }

    private async Task<LibraryWorkspace?> ResolveLibraryAsync(string key)
    {
        var libraries = await LibraryService.GetLibrariesAsync();
        return libraries.FirstOrDefault(library =>
            string.Equals(library.Id.ToString(), key, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(library.Name, key, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(library.RootPath, key, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSubPathOf(string candidatePath, string rootPath)
    {
        var normalizedCandidate = EnsureTrailingSeparator(Path.GetFullPath(candidatePath));
        var normalizedRoot = EnsureTrailingSeparator(Path.GetFullPath(rootPath));
        return normalizedCandidate.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAssetUnderFolder(string libraryRootPath, string folderPath, string assetRelativePath)
    {
        var normalizedFolderPath = Path.GetRelativePath(libraryRootPath, folderPath)
            .Replace('\\', '/')
            .TrimEnd('/');
        if (string.IsNullOrWhiteSpace(normalizedFolderPath) || normalizedFolderPath == ".")
        {
            return false;
        }

        var normalizedAssetPath = assetRelativePath.Replace('\\', '/').TrimStart('/');
        return normalizedAssetPath.Equals(normalizedFolderPath, StringComparison.OrdinalIgnoreCase) ||
               normalizedAssetPath.StartsWith(normalizedFolderPath + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static string EnsureTrailingSeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
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

    private static int? GetIntOptionValue(string[] args, string name)
    {
        var value = GetOptionValue(args, name);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return int.TryParse(value, out var parsed) ? parsed : null;
    }

    private static string? GetLeadingText(string[] args)
    {
        var parts = new List<string>();
        foreach (var arg in args)
        {
            if (arg.StartsWith('-'))
            {
                break;
            }

            parts.Add(arg);
        }

        var text = string.Join(" ", parts).Trim();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private async Task<AssetDescriptionDocument> DescribeSingleAssetAsync(
        ManagedAssetRecord asset,
        string? prompt,
        string? systemPrompt,
        double? rangeStart = null,
        double? rangeEnd = null)
    {
        AssetDescriptionDocument? completedDocument = null;
        Exception? failure = null;

        await DescribeAssetsUseCase.ExecuteAsync(
            [asset],
            BackendLauncher.BaseUrl,
            prompt,
            systemPrompt,
            rangeStart,
            rangeEnd,
            progress: progress =>
            {
                if (progress.Kind == DescribeAssetProgressKind.Completed)
                {
                    completedDocument = progress.Document;
                }
                else if (progress.Kind == DescribeAssetProgressKind.Failed)
                {
                    failure = progress.Error;
                }

                return Task.CompletedTask;
            });

        if (completedDocument is not null)
        {
            return completedDocument;
        }

        throw failure ?? new InvalidOperationException("素材描述失败。");
    }

    private async Task<int> ShowTokenUsageAsync(string[] args)
    {
        var libraryKey = GetOptionValue(args, "--library");
        var assetKey = GetOptionValue(args, "--asset");
        var limit = GetIntOptionValue(args, "--limit") ?? 20;

        long? libraryId = null;
        long? assetId = null;
        string? assetName = null;

        if (!string.IsNullOrWhiteSpace(libraryKey))
        {
            var library = await ResolveLibraryAsync(libraryKey);
            if (library is null)
            {
                Console.Error.WriteLine($"素材库不存在: {libraryKey}");
                return 1;
            }

            libraryId = library.Id;
            if (!string.IsNullOrWhiteSpace(assetKey))
            {
                var assets = await LibraryService.ScanLibraryAsync(library);
                var asset = ResolveAsset(assets, assetKey);
                if (asset is null)
                {
                    Console.Error.WriteLine($"素材不存在: {assetKey}");
                    return 1;
                }

                assetId = asset.DatabaseId;
                assetName = asset.Name;
            }
        }
        else if (!string.IsNullOrWhiteSpace(assetKey))
        {
            Console.Error.WriteLine("--asset 需要与 --library 一起使用。");
            return 1;
        }

        var summary = await DescriptionStore.GetTokenUsageSummaryAsync(assetId, libraryId, limit);
        if (summary.CallCount == 0)
        {
            Console.WriteLine(assetId is not null
                ? $"素材 [{assetName ?? assetId.ToString()}] 暂无 token/费用记录（尚无描述调用或调用未返回用量统计）。"
                : "暂无 token/费用记录（尚无描述调用或调用未返回用量统计）。");
            return 0;
        }

        Console.WriteLine("=== Token/费用统计 ===");
        Console.WriteLine($"- 调用次数: {summary.CallCount:N0} 次");
        Console.WriteLine($"- 输入 token: {summary.TotalInputTokens:N0}");
        Console.WriteLine($"- 输出 token: {summary.TotalOutputTokens:N0}");
        Console.WriteLine($"- 合计 token: {summary.TotalTokens:N0}");
        Console.WriteLine($"- 预估费用: ≈ {summary.TotalCostCny:F4} 元 (CNY, 按 providers.yaml 中 qwen3.7-flash 官网价格估算)");
        Console.WriteLine();
        Console.WriteLine($"最近 {summary.RecentEntries.Count} 次流水：");
        var index = 0;
        foreach (var entry in summary.RecentEntries)
        {
            index++;
            var operation = string.Equals(entry.Operation, "describe", StringComparison.Ordinal)
                ? string.Empty
                : $"[{entry.Operation}] ";
            var model = string.IsNullOrWhiteSpace(entry.Model) ? string.Empty : $" model={entry.Model}";
            var assetLabel = string.Equals(entry.AssetType, "检索", StringComparison.Ordinal)
                ? entry.AssetName
                : $"{entry.AssetName}({entry.AssetType})";
            Console.WriteLine(
                $"#{index} {entry.CreatedAt:yyyy-MM-dd HH:mm:ss} | {operation}{assetLabel} | " +
                $"mode={entry.Mode}{model} | 输入 {entry.InputTokens:N0} / 输出 {entry.OutputTokens:N0} / " +
                $"合计 {entry.TotalTokens:N0} | ≈{entry.EstimatedCostCny:F4} 元");
        }

        return 0;
    }

    private static void PrintDescriptionResult(AssetDescriptionDocument document)
    {
        Console.WriteLine("描述生成完成。");
        Console.WriteLine($"- 素材: {document.AssetName}");
        Console.WriteLine($"- 模式: {document.Mode}");
        Console.WriteLine($"- 时间: {document.GeneratedAt:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine($"- 文本: {document.PrimaryDescription}");
        if (document.TokenUsage is { } usage)
        {
            Console.WriteLine($"- Token: 输入 {usage.InputTokens:N0} / 输出 {usage.OutputTokens:N0} / 合计 {usage.TotalTokens:N0}");
            Console.WriteLine(usage.EstimatedCostCny is { } cost
                ? $"- 费用: ≈ {cost:F4} 元 (CNY, 按 providers.yaml 中 qwen3.7-flash 官网价格估算)"
                : "- 费用: 未配置 pricing，无法估算");
        }
        else
        {
            Console.WriteLine("- Token: 本次调用未返回用量统计");
        }
    }

    private static void PrintSearchResult(AssetSearchResponseDocument response)
    {
        Console.WriteLine("查询完成。");
        Console.WriteLine($"- 查询: {response.Query}");
        Console.WriteLine($"- 候选数: {response.CandidateTopK}");
        Console.WriteLine($"- 返回数: {response.FinalTopK}");
        Console.WriteLine($"- 向量模型: {response.EmbeddingModel}");
        Console.WriteLine($"- 重排模型: {response.RerankModel}");

        for (var index = 0; index < response.Results.Length; index++)
        {
            var item = response.Results[index];
            Console.WriteLine($"[{index + 1}] {item.AssetName} | {item.AssetType} | rerank={item.RerankScore:0.0000} | sim={item.EmbeddingSimilarity:0.0000}");
            Console.WriteLine($"    path: {item.AssetPath}");
            Console.WriteLine($"    desc: {item.Description}");
        }
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
              libraries add <folderPath> [--kind clip|standard]
              libraries scan <libraryId|libraryName|rootPath>
              libraries remove <libraryId|libraryName|rootPath>
              libraries rename <libraryId|libraryName|rootPath> --name <新名称>
              assets describe --library <libraryId|libraryName|rootPath> --asset <assetId|relativePath|fileName> [--prompt <prompt>] [--system-prompt <prompt>] [--start <秒>] [--end <秒>]
              assets describe-dir --library <libraryId|libraryName|rootPath> --folder <relativeFolderPath> [--prompt <prompt>] [--system-prompt <prompt>]
              assets split --library <libraryId|libraryName|rootPath> --asset <assetId|relativePath|fileName> [--start <秒>] [--end <秒>]
              assets vectorize-missing --library <libraryId|libraryName|rootPath>
              assets delete --library <libraryId|libraryName|rootPath> --asset <assetId|relativePath|fileName>
              assets rename --library <libraryId|libraryName|rootPath> --asset <assetId|relativePath|fileName> --name <新名称>
              assets set-type --library <libraryId|libraryName|rootPath> --asset <assetId|relativePath|fileName> --type <视频|视频剪辑>
              assets tag --library <libraryId|libraryName|rootPath> --asset <assetId|relativePath|fileName> [--set <tag1,tag2>] [--add <tag>] [--remove <tag>]
              assets set-description --library <libraryId|libraryName|rootPath> --asset <assetId|relativePath|fileName> (--text <内容> | --file <路径>)
              assets clear-description --library <libraryId|libraryName|rootPath> --asset <assetId|relativePath|fileName>
              assets search <query> [--candidate-top-k <n>] [--top-k <n>] [--format <assetFormat>]
              assets usage [--library <libraryId|libraryName|rootPath>] [--asset <assetId|relativePath|fileName>] [--limit <n>]
              assets reindex

            示例:
              libraries add D:\Data\WebGal
              libraries add D:\Data\Clips --kind clip
              libraries scan 我的素材库
              assets describe --library 我的素材库 --asset background.png
              assets describe --library 我的素材库 --asset intro.mp4 --start 0 --end 30
              assets describe-dir --library 我的素材库 --folder background\bg
              assets set-type --library 我的素材库 --asset intro.mp4 --type 视频剪辑
              assets tag --library 我的素材库 --asset background.png --add 风景
              assets reindex
              assets vectorize-missing --library 我的素材库
            """);
    }

    private static void PrintLibraryHelp()
    {
        Console.WriteLine("""
            libraries 命令:
              libraries list
              libraries add <folderPath> [--kind clip|standard]
              libraries scan <libraryId|libraryName|rootPath|directoryPath>
              libraries remove <libraryId|libraryName|rootPath>
              libraries rename <libraryId|libraryName|rootPath> --name <新名称>
            """);
    }

    private static void PrintAssetHelp()
    {
        Console.WriteLine("""
            assets 命令:
              assets describe --library <libraryId|libraryName|rootPath> --asset <assetId|relativePath|fileName> [--prompt <prompt>] [--system-prompt <prompt>] [--start <秒>] [--end <秒>]
              assets describe-dir --library <libraryId|libraryName|rootPath> --folder <relativeFolderPath> [--prompt <prompt>] [--system-prompt <prompt>]
              assets split --library <libraryId|libraryName|rootPath> --asset <assetId|relativePath|fileName> [--start <秒>] [--end <秒>]
              assets search <query> [--candidate-top-k <n>] [--top-k <n>] [--format <assetFormat>]
              assets reindex
              assets vectorize-missing [--library <libraryId|libraryName|rootPath>]
              assets delete --library <libraryId|libraryName|rootPath> --asset <assetId|relativePath|fileName>
              assets rename --library <libraryId|libraryName|rootPath> --asset <assetId|relativePath|fileName> --name <新名称>
              assets set-type --library <libraryId|libraryName|rootPath> --asset <assetId|relativePath|fileName> --type <视频|视频剪辑>
              assets tag --library <libraryId|libraryName|rootPath> --asset <assetId|relativePath|fileName> [--set <tag1,tag2>] [--add <tag>] [--remove <tag>]
              assets set-description --library <libraryId|libraryName|rootPath> --asset <assetId|relativePath|fileName> (--text <内容> | --file <路径>)
              assets clear-description --library <libraryId|libraryName|rootPath> --asset <assetId|relativePath|fileName>
              assets usage [--library <libraryId|libraryName|rootPath>] [--asset <assetId|relativePath|fileName>] [--limit <n>]
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

    private async Task<int> PrintScanResultAsync(string targetLabel, IReadOnlyList<ManagedAssetRecord> assets)
    {
        Console.WriteLine($"扫描目标：{targetLabel}");
        Console.WriteLine($"素材数量：{assets.Count}");

        // 批量读取描述，避免逐素材 N+1 查询
        var descriptions = await DescriptionStore.GetDescriptionsAsync(
            assets.Select(a => a.DatabaseId).ToList());
        foreach (var asset in assets)
        {
            var description = descriptions.GetValueOrDefault(asset.DatabaseId);
            var descriptionState = description is null ? "未描述" : $"已描述({description.Mode})";
            Console.WriteLine($"- {asset.RelativePath} | {asset.AssetType} | {asset.Stage} | {asset.AiState} | {descriptionState}");
        }

        return 0;
    }
}
