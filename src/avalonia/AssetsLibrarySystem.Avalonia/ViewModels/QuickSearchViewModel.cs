using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using AssetsLibrarySystem.Avalonia.Models;
using AssetsLibrarySystem.Avalonia.Services.AssetSearch;
using AssetsLibrarySystem.Avalonia.Services.BackendLauncher;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

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
        if (AssetSearchService is null)
        {
            SearchStatus = "检索服务未注册，无法调用后端。";
            return;
        }

        if (string.IsNullOrWhiteSpace(SearchQuery))
        {
            SearchStatus = "请输入要检索的文本描述。";
            return;
        }

        try
        {
            if (BackendLauncher is not null && BackendLauncher.IsRunning != true)
            {
                SearchStatus = "正在启动 Python 模型服务...";
                await BackendLauncher.StartAsync();
            }

            SearchStatus = "正在检索...";

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
        }
        catch (System.Exception ex)
        {
            SearchStatus = $"检索失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private void RevealSearchResultInExplorer(AssetSearchDocument? result)
    {
        if (result is null || string.IsNullOrWhiteSpace(result.AssetPath))
        {
            SearchStatus = "当前搜索结果没有可打开的本地路径。";
            return;
        }

        try
        {
            var path = Path.GetFullPath(result.AssetPath);
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
        }
        catch (System.Exception ex)
        {
            SearchStatus = $"定位失败：{ex.Message}";
        }
    }
}
