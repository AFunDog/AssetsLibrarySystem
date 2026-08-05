using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AssetsLibrarySystem.Application.Models;
using AssetsLibrarySystem.Avalonia.Models;
using AssetsLibrarySystem.Application.Services.AssetSearch;
using AssetsLibrarySystem.Application.Services.BackendLauncher;
using AssetsLibrarySystem.Avalonia.Services.Search;
using AssetsLibrarySystem.Avalonia.Services.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace AssetsLibrarySystem.Avalonia.ViewModels;

public partial class QuickSearchViewModel : ObservableObject
{
    private const string AllAssetFormat = "全部";
    private const string SmartAssetFormat = "智能类型";

    private IAssetSearchService? AssetSearchService { get; }
    private IBackendLauncher? BackendLauncher { get; }
    private IUserSettingsService? UserSettingsService { get; }
    private SearchHistoryService SearchHistory { get; }

    /// <summary>检索进行中标记：防止连按回车并发重入导致结果竞态覆盖</summary>
    private bool _isSearching;

    public QuickSearchViewModel()
        : this(null, null, null, null)
    {
    }

    public QuickSearchViewModel(
        IBackendLauncher? backendLauncher,
        IAssetSearchService? assetSearchService,
        IUserSettingsService? userSettingsService,
        SearchHistoryService? searchHistoryService)
    {
        BackendLauncher = backendLauncher;
        AssetSearchService = assetSearchService;
        UserSettingsService = userSettingsService;
        SearchHistory = searchHistoryService ?? new SearchHistoryService();
        SearchResults = new ObservableCollection<AssetSearchDocument>();
        SearchAssetFormats = [AllAssetFormat, SmartAssetFormat, "文本", "图片", "视频", "音频", "视频剪辑"];
        SearchStatus = "输入素材描述并按回车检索，点击卡片可定位到素材文件。";
        SearchMetricsSummary = $"参数：候选 {SearchCandidateTopK}，扩展候选 {SearchExpandedCandidateTopK}，Top-K {SearchFinalTopK}；重排 {SearchRerankTopK}；类型：全部。";
        SearchQuery = string.Empty;
        SearchAssetFormat = AllAssetFormat;
        Log.Debug("QuickSearchViewModel 已创建，backendLauncherRegistered={HasBackendLauncher}, searchServiceRegistered={HasSearchService}, settingsRegistered={HasSettings}", BackendLauncher is not null, AssetSearchService is not null, UserSettingsService is not null);
    }

    public ObservableCollection<AssetSearchDocument> SearchResults { get; }
    public IReadOnlyList<string> SearchAssetFormats { get; }

    private int SearchCandidateTopK => UserSettingsService?.SearchCandidateTopK ?? 20;
    private int SearchExpandedCandidateTopK => UserSettingsService?.SearchExpandedCandidateTopK ?? 160;
    private int SearchRerankTopK => UserSettingsService?.SearchRerankTopK ?? 50;
    private int SearchFinalTopK => UserSettingsService?.SearchFinalTopK ?? 5;

    [ObservableProperty]
    public partial string SearchQuery { get; set; }

    [ObservableProperty]
    public partial string SearchAssetFormat { get; set; }

    [ObservableProperty]
    public partial string SearchStatus { get; set; }

    [ObservableProperty]
    public partial string SearchMetricsSummary { get; set; }

    // ===== 搜索历史 =====

    /// <summary>是否显示历史建议下拉列表</summary>
    [ObservableProperty]
    public partial bool IsHistoryDropdownVisible { get; set; }

    /// <summary>当前筛选后的历史建议列表</summary>
    public ObservableCollection<string> HistorySuggestions { get; } = [];

    /// <summary>从历史中选择一条建议并回填搜索</summary>
    [RelayCommand]
    private void SelectHistorySuggestion(string? suggestion)
    {
        if (string.IsNullOrWhiteSpace(suggestion))
            return;

        SearchQuery = suggestion;
        IsHistoryDropdownVisible = false;
        _ = ExecuteSearchAsync();
    }

    /// <summary>清空搜索历史</summary>
    [RelayCommand]
    private void ClearSearchHistory()
    {
        SearchHistory.ClearHistory();
        HistorySuggestions.Clear();
        IsHistoryDropdownVisible = false;
    }

    /// <summary>删除单条历史记录</summary>
    [RelayCommand]
    private void RemoveHistoryItem(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return;

        SearchHistory.RemoveQuery(query);
        HistorySuggestions.Remove(query);
        if (HistorySuggestions.Count == 0)
            IsHistoryDropdownVisible = false;
    }

    /// <summary>更新历史建议列表（在文本变化时调用）</summary>
    public void UpdateHistorySuggestions(string? input)
    {
        var suggestions = SearchHistory.GetSuggestions(input);
        HistorySuggestions.Clear();
        foreach (var s in suggestions)
            HistorySuggestions.Add(s);
        IsHistoryDropdownVisible = suggestions.Count > 0;
    }

    [RelayCommand]
    private async Task ExecuteSearchAsync()
    {
        if (_isSearching)
        {
            Log.Debug("快速检索已在进行中，忽略重复触发。");
            return;
        }

        var candidateTopK = SearchCandidateTopK;
        var expandedCandidateTopK = SearchExpandedCandidateTopK;
        var rerankTopK = SearchRerankTopK;
        var finalTopK = SearchFinalTopK;

        Log.Information(
            "用户在快速检索窗发起搜索: queryLength={QueryLength}, assetFormat={AssetFormat}, candidateTopK={CandidateTopK}, expandedCandidateTopK={ExpandedCandidateTopK}, rerankTopK={RerankTopK}, finalTopK={FinalTopK}",
            SearchQuery?.Length ?? 0,
            string.IsNullOrWhiteSpace(SearchAssetFormat) ? AllAssetFormat : SearchAssetFormat,
            candidateTopK,
            expandedCandidateTopK,
            rerankTopK,
            finalTopK);

        if (AssetSearchService is null)
        {
            SearchStatus = "检索服务未注册，无法调用后端。";
            Log.Warning("快速检索失败：检索服务未注册。");
            return;
        }

        if (string.IsNullOrWhiteSpace(SearchQuery))
        {
            SearchStatus = "请输入要检索的文本描述。";
            Log.Warning("快速检索失败：查询文本为空。");
            return;
        }

        try
        {
            _isSearching = true;

            if (BackendLauncher is not null && BackendLauncher.IsRunning != true)
            {
                SearchStatus = "正在启动 Python 模型服务...";
                Log.Information("快速检索前发现后端未运行，准备启动。");
                await BackendLauncher.StartAsync();
                Log.Information("快速检索前后端启动完成，baseUrl={BaseUrl}", BackendLauncher.BaseUrl);
            }

            SearchStatus = "正在检索...";
            SearchMetricsSummary = $"参数：候选 {candidateTopK}，扩展候选 {expandedCandidateTopK}，Top-K {finalTopK}；重排 {rerankTopK}；类型：{SearchAssetFormat}。";
            Log.Information(
                "开始调用后端检索接口。candidateTopK={CandidateTopK}, expandedCandidateTopK={ExpandedCandidateTopK}, rerankTopK={RerankTopK}, finalTopK={FinalTopK}, assetFormat={AssetFormat}",
                candidateTopK,
                expandedCandidateTopK,
                rerankTopK,
                finalTopK,
                SearchAssetFormat);

            var response = await AssetSearchService.SearchAsync(
                BackendLauncher?.BaseUrl ?? "in-process",
                SearchQuery,
                candidateTopK,
                finalTopK,
                ToServiceAssetFormat(SearchAssetFormat),
                expandedCandidateTopK,
                rerankTopK);

            SearchResults.Clear();
            foreach (var item in response.Results)
            {
                // 预计算高亮分段
                item.HighlightedDescription = StructuredDescriptionHelper.HighlightMatches(
                    item.Description, SearchQuery);
                SearchResults.Add(item);
            }

            // 记录搜索历史
            SearchHistory.AddQuery(SearchQuery);
            IsHistoryDropdownVisible = false;

            SearchStatus = response.Results.Length == 0
                ? "没有找到匹配的素材。"
                : $"已返回 {response.Results.Length} 条素材。";
            SearchMetricsSummary = BuildSearchMetricsSummary(response);
            Log.Information(
                "快速检索完成: resultCount={ResultCount}, queryLength={QueryLength}, assetFormatMode={AssetFormatMode}, resolvedAssetFormat={ResolvedAssetFormat}, candidateTopK={CandidateTopK}, expandedCandidateTopK={ExpandedCandidateTopK}, finalTopK={FinalTopK}, vectorCandidates={VectorCandidates}, rerankCandidates={RerankCandidates}, elapsedMs={ElapsedMs}, embeddingTokenUsage={EmbeddingTokenUsage}, rerankTokenUsage={RerankTokenUsage}",
                response.Results.Length,
                SearchQuery.Length,
                response.AssetFormatMode,
                response.AssetFormat ?? "(all)",
                response.CandidateTopK,
                response.ExpandedCandidateTopK,
                response.FinalTopK,
                response.VectorCandidateCount,
                response.RerankCandidateCount,
                response.ElapsedMs,
                response.EmbeddingTokenUsage,
                response.RerankTokenUsage);
        }
        catch (System.Exception ex)
        {
            SearchStatus = $"检索失败：{ex.Message}";
            SearchMetricsSummary = $"参数：候选 {candidateTopK}，扩展候选 {expandedCandidateTopK}，Top-K {finalTopK}；重排 {rerankTopK}；类型：{SearchAssetFormat}。";
            Log.Error(ex, "快速检索失败。");
        }
        finally
        {
            _isSearching = false;
        }
    }

    [RelayCommand]
    private void RevealSearchResultInExplorer(AssetSearchDocument? result)
    {
        if (result is null || string.IsNullOrWhiteSpace(result.AssetPath))
        {
            SearchStatus = "当前搜索结果没有可打开的本地路径。";
            Log.Warning("快速检索定位失败：结果没有可用路径。");
            return;
        }

        try
        {
            var path = Path.GetFullPath(result.AssetPath);
            Log.Information("用户点击搜索结果定位到资源管理器: assetName={AssetName}, assetPath={AssetPath}", result.AssetName, path);
            var startInfo = new ProcessStartInfo
            {
                FileName = "explorer.exe",
                UseShellExecute = true,
            };

            if (File.Exists(path))
            {
                startInfo.Arguments = $"/select,\"{path}\"";
            }
            else
            {
                startInfo.Arguments = $"\"{path}\"";
            }

            Process.Start(startInfo);
            SearchStatus = $"已在文件资源管理器中定位：{result.AssetName}";
            Log.Information("资源管理器定位成功: assetName={AssetName}, path={Path}", result.AssetName, path);
        }
        catch (System.Exception ex)
        {
            SearchStatus = $"定位失败：{ex.Message}";
            Log.Error(ex, "资源管理器定位失败: assetName={AssetName}", result.AssetName);
        }
    }

    private static string? ToServiceAssetFormat(string? selectedAssetFormat)
    {
        return string.IsNullOrWhiteSpace(selectedAssetFormat) || selectedAssetFormat == AllAssetFormat
            ? null
            : selectedAssetFormat;
    }

    private static string BuildSearchMetricsSummary(AssetSearchResponseDocument response)
    {
        var embeddingTokenText = FormatTokenUsage("向量化", response.EmbeddingTokenUsage);
        var rerankTokenText = FormatTokenUsage("重排序", response.RerankTokenUsage);
        var filterText = response.AssetFormat is null ? "全部" : response.AssetFormat;
        return $"参数：候选 {response.CandidateTopK}，扩展候选 {response.ExpandedCandidateTopK}，Top-K {response.FinalTopK}；" +
               $"类型：{FormatAssetFormatMode(response.AssetFormatMode)} / {filterText}；" +
               $"召回 {response.VectorCandidateCount}，重排 {response.RerankCandidateCount}，返回 {response.ReturnedCount}；" +
               $"耗时 {response.ElapsedMs:0} ms，{embeddingTokenText}，{rerankTokenText}。";
    }

    private static string FormatTokenUsage(string stage, int? tokenUsage) =>
        tokenUsage is null ? $"{stage} token 未返回" : $"{stage} token {tokenUsage}";

    private static string FormatAssetFormatMode(string mode) => mode switch
    {
        "smart" => "智能类型",
        "explicit" => "手动类型",
        _ => "全部",
    };
}
