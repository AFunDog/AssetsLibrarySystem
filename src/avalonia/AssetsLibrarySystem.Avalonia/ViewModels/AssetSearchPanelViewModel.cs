using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using AssetsLibrarySystem.Application.Models;
using AssetsLibrarySystem.Application.Services.AssetSearch;
using AssetsLibrarySystem.Application.UseCases.AssetOperations;
using AssetsLibrarySystem.Avalonia.Models;
using AssetsLibrarySystem.Avalonia.Services.Activity;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace AssetsLibrarySystem.Avalonia.ViewModels;

public sealed partial class AssetSearchPanelViewModel : ObservableObject
{
    private BackendStatusViewModel BackendStatus { get; }
    private LibraryWorkspaceViewModel Workspace { get; }
    private IAssetSearchService? AssetSearchService { get; }
    private RebuildSearchIndexUseCase? RebuildSearchIndexUseCase { get; }
    private ActivityFeedService _activityFeedService;

    public AssetSearchPanelViewModel()
        : this(new BackendStatusViewModel(), new LibraryWorkspaceViewModel(), null, null, new ActivityFeedService())
    {
    }

    public AssetSearchPanelViewModel(
        BackendStatusViewModel backendStatus,
        LibraryWorkspaceViewModel workspace,
        IAssetSearchService? assetSearchService,
        RebuildSearchIndexUseCase? rebuildSearchIndexUseCase,
        ActivityFeedService activityFeedService)
    {
        BackendStatus = backendStatus;
        Workspace = workspace;
        AssetSearchService = assetSearchService;
        RebuildSearchIndexUseCase = rebuildSearchIndexUseCase;
        _activityFeedService = activityFeedService;

        SearchResults = [];
        SearchQuery = "紧张氛围的音乐";
        SearchCandidateTopKText = "20";
        SearchFinalTopKText = "5";
        SearchAssetFormats = ["全部", "智能类型", "文本", "图片", "视频", "音频", "视频剪辑"];
        SearchAssetFormat = string.Empty;
        SearchStatus = "尚未执行素材检索。";
        SearchIndexSummary = "尚未刷新本地检索状态。";
        SearchIndexDetail = "当前由桌面端直接读取 asset_descriptions.db 做本地召回，Python 后端只负责 embedding 和 rerank。";

        ExecuteSearchCommand = new AsyncRelayCommand(ExecuteSearchAsync);
        RebuildSearchIndexCommand = new AsyncRelayCommand(RebuildSearchIndexAsync);
    }

    public ObservableCollection<AssetSearchDocument> SearchResults { get; }
    public IReadOnlyList<string> SearchAssetFormats { get; }

    /// <summary>是否存在搜索结果（控制结果列表可见性）</summary>
    public bool HasSearchResults => SearchResults.Count > 0;

    [ObservableProperty]
    public partial string SearchQuery { get; set; }

    [ObservableProperty]
    public partial string SearchCandidateTopKText { get; set; }

    [ObservableProperty]
    public partial string SearchFinalTopKText { get; set; }

    [ObservableProperty]
    public partial string SearchAssetFormat { get; set; }

    [ObservableProperty]
    public partial string SearchStatus { get; set; }

    [ObservableProperty]
    public partial string SearchIndexSummary { get; set; }

    [ObservableProperty]
    public partial string SearchIndexDetail { get; set; }

    public IAsyncRelayCommand ExecuteSearchCommand { get; }
    public IAsyncRelayCommand RebuildSearchIndexCommand { get; }

    public void RevealSearchResultInExplorer(AssetSearchDocument? result)
    {
        if (result is null || string.IsNullOrWhiteSpace(result.AssetPath))
        {
            Workspace.SetOperatorNotice("当前搜索结果没有可打开的本地路径。");
            Log.Warning("搜索结果定位失败：没有可用路径。");
            return;
        }

        var path = Path.GetFullPath(result.AssetPath);
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                UseShellExecute = true,
                Arguments = $"/select,\"{path}\"",
            });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "打开资源管理器失败: path={Path}", path);
            Workspace.SetOperatorNotice($"打开文件资源管理器失败：{ex.Message}");
            return;
        }

        Workspace.SetOperatorNotice($"已在文件资源管理器中显示：{path}");
        _activityFeedService.Add($"搜索结果定位：{result.AssetName}");
        Log.Information("搜索结果定位成功: assetName={AssetName}, assetPath={AssetPath}", result.AssetName, path);
    }

    private async Task ExecuteSearchAsync()
    {
        if (!BackendStatus.IsBackendReady)
        {
            Workspace.SetOperatorNotice("Python 模型服务尚未就绪，请先等待后端启动完成。");
            SearchStatus = "后端未就绪，无法执行检索。";
            Log.Warning("用户触发库页检索，但后端未就绪。");
            return;
        }

        if (AssetSearchService is null)
        {
            Workspace.SetOperatorNotice("检索服务未注册，当前无法调用后端。");
            SearchStatus = "检索服务未注册。";
            Log.Warning("库页检索失败：检索服务未注册。");
            return;
        }

        if (string.IsNullOrWhiteSpace(SearchQuery))
        {
            Workspace.SetOperatorNotice("请输入要检索的文本描述。");
            SearchStatus = "请输入查询文本。";
            Log.Warning("库页检索失败：查询文本为空。");
            return;
        }

        if (!TryParsePositiveInt(SearchCandidateTopKText, out var candidateTopK))
        {
            Workspace.SetOperatorNotice("候选数量必须是大于 0 的整数。");
            SearchStatus = "候选数量格式错误。";
            Log.Warning("库页检索失败：候选数量格式错误，value={Value}", SearchCandidateTopKText);
            return;
        }

        if (!TryParsePositiveInt(SearchFinalTopKText, out var finalTopK))
        {
            Workspace.SetOperatorNotice("返回数量必须是大于 0 的整数。");
            SearchStatus = "返回数量格式错误。";
            Log.Warning("库页检索失败：返回数量格式错误，value={Value}", SearchFinalTopKText);
            return;
        }

        SearchStatus = "正在执行向量召回与重排序...";
        Workspace.SetOperatorNotice($"正在检索：“{SearchQuery}”");
        _activityFeedService.Add($"开始检索：{SearchQuery}");
        Log.Information(
            "用户在库页发起检索: queryLength={QueryLength}, candidateTopK={CandidateTopK}, finalTopK={FinalTopK}, assetFormat={AssetFormat}",
            SearchQuery.Length,
            candidateTopK,
            finalTopK,
            string.IsNullOrWhiteSpace(SearchAssetFormat) ? "全部" : SearchAssetFormat);

        try
        {
            var response = await AssetSearchService.SearchAsync(
                BackendStatus.BaseUrl,
                SearchQuery,
                candidateTopK,
                finalTopK,
                string.IsNullOrWhiteSpace(SearchAssetFormat) || SearchAssetFormat == "全部" ? null : SearchAssetFormat);

            SearchResults.Clear();
            foreach (var item in response.Results)
            {
                SearchResults.Add(item);
            }

            OnPropertyChanged(nameof(HasSearchResults));

            SearchStatus = $"检索完成：候选 {response.CandidateTopK} 条，返回 {response.Results.Length} 条。";
            Workspace.SetOperatorNotice(SearchStatus);
            _activityFeedService.Add($"检索完成：{SearchQuery} -> {response.Results.Length} 条结果");
            Log.Information(
                "库页检索完成: resultCount={ResultCount}, embeddingModel={EmbeddingModel}, rerankModel={RerankModel}",
                response.Results.Length,
                response.EmbeddingModel,
                response.RerankModel);
        }
        catch (Exception ex)
        {
            SearchStatus = $"检索失败：{ex.Message}";
            Workspace.SetOperatorNotice(SearchStatus);
            _activityFeedService.Add($"检索失败：{SearchQuery} -> {ex.Message}");
            Log.Error(ex, "库页检索失败。");
        }
    }

    private async Task RebuildSearchIndexAsync()
    {
        if (RebuildSearchIndexUseCase is null)
        {
            SearchIndexSummary = "检索服务未注册，无法重建索引。";
            Workspace.SetOperatorNotice(SearchIndexSummary);
            Log.Warning("用户触发索引重建，但检索服务未注册。");
            return;
        }

        SearchIndexSummary = "正在重建向量索引...";
        SearchIndexDetail = "桌面端会重新扫描本地 SQLite 向量数据；当前阶段不再依赖 Python 后端读库建索引。";
        Workspace.SetOperatorNotice(SearchIndexSummary);
        _activityFeedService.Add("开始重建向量索引。");
        Log.Information("用户触发向量索引重建。");

        try
        {
            var response = await RebuildSearchIndexUseCase.ExecuteAsync();
            SearchIndexSummary = $"索引已重建：{response.DocumentCount} 条，{response.VectorDim} 维。";
            SearchIndexDetail = $"数据库：{response.DatabasePath}\n索引：{response.IndexPath}\n元数据：{response.MetadataPath}\n模型：{string.Join(", ", response.EmbeddingModels)}";
            Workspace.SetOperatorNotice(SearchIndexSummary);
            _activityFeedService.Add($"索引重建完成：{response.DocumentCount} 条素材描述。");
            Log.Information(
                "索引重建完成: documentCount={DocumentCount}, vectorDim={VectorDim}, databasePath={DatabasePath}, indexPath={IndexPath}",
                response.DocumentCount,
                response.VectorDim,
                response.DatabasePath,
                response.IndexPath);
        }
        catch (Exception ex)
        {
            SearchIndexSummary = $"索引重建失败：{ex.Message}";
            SearchIndexDetail = ex.Message;
            Workspace.SetOperatorNotice(SearchIndexSummary);
            _activityFeedService.Add($"索引重建失败：{ex.Message}");
            Log.Error(ex, "索引重建失败。");
        }
    }

    private static bool TryParsePositiveInt(string? value, out int result)
    {
        if (int.TryParse(value, out result) && result > 0)
        {
            return true;
        }

        result = 0;
        return false;
    }
}
