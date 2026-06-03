using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using AssetsLibrarySystem.Application.Models;
using AssetsLibrarySystem.Avalonia.Models;
using AssetsLibrarySystem.Application.Services.AssetSearch;
using AssetsLibrarySystem.Application.Services.BackendLauncher;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace AssetsLibrarySystem.Avalonia.ViewModels;

public partial class QuickSearchViewModel : ObservableObject
{
    private IAssetSearchService? AssetSearchService { get; }
    private IBackendLauncher? BackendLauncher { get; }

    public QuickSearchViewModel()
        : this(null, null)
    {
    }

    public QuickSearchViewModel(IBackendLauncher? backendLauncher, IAssetSearchService? assetSearchService)
    {
        BackendLauncher = backendLauncher;
        AssetSearchService = assetSearchService;
        SearchResults = new ObservableCollection<AssetSearchDocument>();
        SearchAssetFormats = ["全部", "文本", "图片", "视频", "音频"];
        SearchStatus = "输入素材描述并按回车检索，点击卡片可定位到素材文件。";
        SearchQuery = string.Empty;
        SearchAssetFormat = "全部";
        Log.Debug("QuickSearchViewModel 已创建，backendLauncherRegistered={HasBackendLauncher}, searchServiceRegistered={HasSearchService}", BackendLauncher is not null, AssetSearchService is not null);
    }

    public ObservableCollection<AssetSearchDocument> SearchResults { get; }
    public IReadOnlyList<string> SearchAssetFormats { get; }

    [ObservableProperty]
    public partial string SearchQuery { get; set; }

    [ObservableProperty]
    public partial string SearchAssetFormat { get; set; }

    [ObservableProperty]
    public partial string SearchStatus { get; set; }

    [RelayCommand]
    private async Task ExecuteSearchAsync()
    {
        Log.Information(
            "用户在快速检索窗发起搜索: queryLength={QueryLength}, assetFormat={AssetFormat}",
            SearchQuery?.Length ?? 0,
            string.IsNullOrWhiteSpace(SearchAssetFormat) ? "全部" : SearchAssetFormat);

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
            if (BackendLauncher is not null && BackendLauncher.IsRunning != true)
            {
                SearchStatus = "正在启动 Python 模型服务...";
                Log.Information("快速检索前发现后端未运行，准备启动。");
                await BackendLauncher.StartAsync();
                Log.Information("快速检索前后端启动完成，baseUrl={BaseUrl}", BackendLauncher.BaseUrl);
            }

            SearchStatus = "正在检索...";
            Log.Information("开始调用后端检索接口。");

            var response = await AssetSearchService.SearchAsync(
                BackendLauncher?.BaseUrl ?? "http://127.0.0.1:8000",
                SearchQuery,
                20,
                5,
                SearchAssetFormat == "全部" ? null : SearchAssetFormat);

            SearchResults.Clear();
            foreach (var item in response.Results)
            {
                SearchResults.Add(item);
            }

            SearchStatus = response.Results.Length == 0
                ? "没有找到匹配的素材。"
                : $"已返回 {response.Results.Length} 条素材。";
            Log.Information(
                "快速检索完成: resultCount={ResultCount}, queryLength={QueryLength}, assetFormat={AssetFormat}",
                response.Results.Length,
                SearchQuery.Length,
                string.IsNullOrWhiteSpace(SearchAssetFormat) ? "全部" : SearchAssetFormat);
        }
        catch (System.Exception ex)
        {
            SearchStatus = $"检索失败：{ex.Message}";
            Log.Error(ex, "快速检索失败。");
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
}
