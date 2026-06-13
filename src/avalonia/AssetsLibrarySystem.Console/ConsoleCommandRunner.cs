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

    public ConsoleCommandRunner(
        IAssetLibraryService libraryService,
        IAssetDescriptionStore descriptionStore,
        IAssetDescriptionVectorStore vectorStore,
        IAssetSearchService assetSearchService,
        IBackendLauncher backendLauncher,
        DescribeAssetsUseCase describeAssetsUseCase,
        VectorizeDescriptionsUseCase vectorizeDescriptionsUseCase,
        RebuildSearchIndexUseCase rebuildSearchIndexUseCase)
    {
        LibraryService = libraryService;
        DescriptionStore = descriptionStore;
        VectorStore = vectorStore;
        AssetSearchService = assetSearchService;
        BackendLauncher = backendLauncher;
        DescribeAssetsUseCase = describeAssetsUseCase;
        VectorizeDescriptionsUseCase = vectorizeDescriptionsUseCase;
        RebuildSearchIndexUseCase = rebuildSearchIndexUseCase;
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
            "describe-dir" => await DescribeDirectoryAsync(args.Skip(1).ToArray()),
            "vectorize-missing" => await VectorizeMissingDescriptionsAsync(args.Skip(1).ToArray()),
            "search" => await SearchAssetsAsync(args.Skip(1).ToArray()),
            "query" => await SearchAssetsAsync(args.Skip(1).ToArray()),
            "reindex" => await ReindexSearchIndexAsync(args.Skip(1).ToArray()),
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
            var document = await DescribeSingleAssetAsync(asset, prompt, systemPrompt);
            PrintDescriptionResult(document);
        }
        finally
        {
            await BackendLauncher.StopAsync();
        }

        return 0;
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
                progress =>
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

                if ((await VectorStore.ListByAssetIdAsync(asset.DatabaseId)).Count > 0)
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
        string? systemPrompt)
    {
        AssetDescriptionDocument? completedDocument = null;
        Exception? failure = null;

        await DescribeAssetsUseCase.ExecuteAsync(
            [asset],
            BackendLauncher.BaseUrl,
            prompt,
            systemPrompt,
            progress =>
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

    private static void PrintDescriptionResult(AssetDescriptionDocument document)
    {
        Console.WriteLine("描述生成完成。");
        Console.WriteLine($"- 素材: {document.AssetName}");
        Console.WriteLine($"- 模式: {document.Mode}");
        Console.WriteLine($"- 时间: {document.GeneratedAt:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine($"- 文本: {document.PrimaryDescription}");
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
              libraries add <folderPath>
              libraries scan <libraryId|libraryName|rootPath>
              assets describe --library <libraryId|libraryName|rootPath> --asset <assetId|relativePath|fileName> [--prompt <prompt>] [--system-prompt <prompt>]
              assets describe-dir --library <libraryId|libraryName|rootPath> --folder <relativeFolderPath> [--prompt <prompt>] [--system-prompt <prompt>]
              assets vectorize-missing --library <libraryId|libraryName|rootPath>

            示例:
              libraries add D:\Data\WebGal
              libraries scan 我的素材库
              assets describe --library 我的素材库 --asset background.png
              assets describe-dir --library 我的素材库 --folder background\bg
              assets reindex
              assets vectorize-missing --library 我的素材库
            """);
    }

    private static void PrintLibraryHelp()
    {
        Console.WriteLine("""
            libraries 命令:
              libraries list
              libraries add <folderPath>
              libraries scan <libraryId|libraryName|rootPath|directoryPath>
            """);
    }

    private static void PrintAssetHelp()
    {
        Console.WriteLine("""
            assets 命令:
              assets describe --library <libraryId|libraryName|rootPath> --asset <assetId|relativePath|fileName> [--prompt <prompt>] [--system-prompt <prompt>]
              assets describe-dir --library <libraryId|libraryName|rootPath> --folder <relativeFolderPath> [--prompt <prompt>] [--system-prompt <prompt>]
              assets search <query> [--candidate-top-k <n>] [--top-k <n>] [--format <assetFormat>]
              assets reindex
              assets vectorize-missing [--library <libraryId|libraryName|rootPath>]
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

        foreach (var asset in assets)
        {
            var description = await DescriptionStore.TryGetAsync(asset.DatabaseId);
            var descriptionState = description is null ? "未描述" : $"已描述({description.Mode})";
            Console.WriteLine($"- {asset.RelativePath} | {asset.AssetType} | {asset.Stage} | {asset.AiState} | {descriptionState}");
        }

        return 0;
    }
}
