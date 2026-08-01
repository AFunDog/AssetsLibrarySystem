using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using AssetsLibrarySystem.Application.Models;
using AssetsLibrarySystem.Application.UseCases.AssetOperations;
using AssetsLibrarySystem.Avalonia.Models;
using AssetsLibrarySystem.Avalonia.Services.Activity;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace AssetsLibrarySystem.Avalonia.ViewModels;

public sealed partial class AssetDescriptionPanelViewModel : ObservableObject
{
    private BackendStatusViewModel BackendStatus { get; }
    private LibraryWorkspaceViewModel Workspace { get; }
    private DescribeAssetsUseCase? DescribeAssetsUseCase { get; }
    private DeleteAssetDescriptionUseCase? DeleteAssetDescriptionUseCase { get; }
    private SplitClipSegmentsUseCase? SplitClipSegmentsUseCase { get; }
    private ActivityFeedService _activityFeedService;

    public AssetDescriptionPanelViewModel()
        : this(new BackendStatusViewModel(), new LibraryWorkspaceViewModel(), null, null, new ActivityFeedService())
    {
    }

    public AssetDescriptionPanelViewModel(
        BackendStatusViewModel backendStatus,
        LibraryWorkspaceViewModel workspace,
        DescribeAssetsUseCase? describeAssetsUseCase,
        DeleteAssetDescriptionUseCase? deleteAssetDescriptionUseCase,
        ActivityFeedService activityFeedService,
        SplitClipSegmentsUseCase? splitClipSegmentsUseCase = null)
    {
        BackendStatus = backendStatus;
        Workspace = workspace;
        DescribeAssetsUseCase = describeAssetsUseCase;
        DeleteAssetDescriptionUseCase = deleteAssetDescriptionUseCase;
        SplitClipSegmentsUseCase = splitClipSegmentsUseCase;
        _activityFeedService = activityFeedService;

        QueueDescriptionsForSelectionCommand = new AsyncRelayCommand(QueueDescriptionsForSelectionAsync);
        QueueSelectedDescriptionCommand = new AsyncRelayCommand(QueueSelectedDescriptionAsync);
        DeleteSelectedDescriptionCommand = new AsyncRelayCommand(DeleteSelectedDescriptionAsync);
        SplitSelectedCommand = new AsyncRelayCommand(SplitSelectedAsync);

        // 剪辑素材识别：跟随工作台选中素材变化
        Workspace.PropertyChanged += OnWorkspacePropertyChanged;
        UpdateClipAssetSelected();
    }

    private void OnWorkspacePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LibraryWorkspaceViewModel.SelectedAsset))
        {
            UpdateClipAssetSelected();
        }
    }

    private void UpdateClipAssetSelected()
    {
        IsClipAssetSelected = Workspace.SelectedAsset is { AssetType: "视频剪辑" };
    }

    public IAsyncRelayCommand QueueDescriptionsForSelectionCommand { get; }
    public IAsyncRelayCommand QueueSelectedDescriptionCommand { get; }
    public IAsyncRelayCommand DeleteSelectedDescriptionCommand { get; }

    /// <summary>仅场景分割（剪辑素材）：保存片段时间点，不调用 LLM</summary>
    public IAsyncRelayCommand SplitSelectedCommand { get; }

    /// <summary>对节点素材执行仅分割（右键菜单），无时间范围</summary>
    public async Task SplitClipForNodeAsync(AssetLibraryTreeNode? node)
    {
        if (node?.Asset is not { AssetType: "视频剪辑" } asset)
        {
            Workspace.SetOperatorNotice("请右键具体的视频剪辑素材，再执行场景分割。");
            return;
        }

        Workspace.SelectedAsset = asset;
        await SplitAssetsAsync([asset], null, null);
    }

    private async Task SplitSelectedAsync()
    {
        if (Workspace.SelectedAsset is not { AssetType: "视频剪辑" } asset)
        {
            Workspace.SetOperatorNotice("请先选择一个视频剪辑素材。");
            return;
        }

        double? rangeStart = null;
        double? rangeEnd = null;
        if (HasTimeRange)
        {
            rangeStart = RangeStartSeconds;
            rangeEnd = RangeEndSeconds;
            if (rangeStart is null || rangeEnd is null || rangeEnd.Value <= rangeStart.Value)
            {
                Workspace.SetOperatorNotice("时间范围无效：请输入开始与结束时间（秒或 mm:ss），且结束需大于开始。");
                return;
            }
        }

        await SplitAssetsAsync([asset], rangeStart, rangeEnd);
    }

    private async Task SplitAssetsAsync(
        IReadOnlyList<ManagedAssetRecord> assets,
        double? rangeStart,
        double? rangeEnd)
    {
        if (!BackendStatus.IsBackendReady)
        {
            Workspace.SetOperatorNotice("Python 模型服务尚未就绪，请先等待后端启动完成。");
            return;
        }

        if (SplitClipSegmentsUseCase is null)
        {
            Workspace.SetOperatorNotice("分割服务未注册，当前无法执行场景分割。");
            return;
        }

        try
        {
            var result = await SplitClipSegmentsUseCase.ExecuteAsync(
                assets,
                BackendStatus.BaseUrl,
                rangeStart,
                rangeEnd,
                progress: progress =>
                {
                    // UseCase 内部 ConfigureAwait(false) 后回调在后台线程执行，
                    // 统一派发到 UI 线程再刷新工作台状态，避免跨线程改集合。
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (progress.Kind == SplitClipProgressKind.Completed)
                        {
                            // fire-and-forget：内部自行 await 读取并刷新，无需等待
                            _ = Workspace.RefreshAssetDescriptionAfterSplit(progress.Asset, progress.SegmentCount ?? 0);
                        }
                        else if (progress.Kind == SplitClipProgressKind.Failed && progress.Error is not null)
                        {
                            Workspace.FailAssetDescription(progress.Asset, progress.Error.Message);
                        }
                    });

                    return Task.CompletedTask;
                });

            var rangeSuffix = rangeStart is not null
                ? $"（范围 {rangeStart.Value:0.##}s-{rangeEnd!.Value:0.##}s）"
                : string.Empty;
            Workspace.SetOperatorNotice(
                $"分割任务完成：新增 {result.SuccessCount}，跳过 {result.SkipCount}，失败 {result.FailureCount}。{rangeSuffix}");
            _activityFeedService.Add(
                $"分割任务完成：新增 {result.SuccessCount}，跳过 {result.SkipCount}，失败 {result.FailureCount}。{rangeSuffix}");
        }
        catch (OperationCanceledException)
        {
            Workspace.SetOperatorNotice("分割任务已取消。");
            _activityFeedService.Add("分割任务已取消");
        }
        catch (Exception ex)
        {
            Workspace.SetOperatorNotice($"分割任务失败：{ex.Message}");
            _activityFeedService.Add($"分割任务失败：{ex.Message}");
            Log.Error(ex, "分割任务失败，assetCount={AssetCount}", assets.Count);
        }
    }

    public async Task QueueDescriptionForNodeAsync(AssetLibraryTreeNode? node)
    {
        if (node is null)
        {
            Workspace.SetOperatorNotice("请先选择一个素材库、目录或素材文件。");
            return;
        }

        Workspace.SelectedAssetTreeNode = node;
        var assets = Workspace.GetDescriptionSelectionAssets();
        if (assets.Count == 0)
        {
            Workspace.SetOperatorNotice("当前节点下没有可发送到后端描述的素材。");
            Log.Warning(
                "右键加入描述任务失败：节点下没有素材，nodeName={NodeName}, nodeKind={NodeKind}, path={Path}",
                node.DisplayName,
                node.Kind,
                node.FullPath);
            return;
        }

        if (!BackendStatus.IsBackendReady)
        {
            Workspace.SetOperatorNotice("Python 模型服务尚未就绪，请先等待后端启动完成。");
            Log.Warning("右键加入描述任务失败：后端未就绪，assetCount={AssetCount}", assets.Count);
            return;
        }

        if (DescribeAssetsUseCase is null)
        {
            Workspace.SetOperatorNotice("描述服务未注册，当前无法调用后端。");
            Log.Warning("右键加入描述任务失败：描述服务未注册，assetCount={AssetCount}", assets.Count);
            return;
        }

        Workspace.SetOperatorNotice($"已将 {assets.Count} 个素材排入后端描述任务。");
        _activityFeedService.Add($"右键描述任务排队：{node.DisplayName}，共 {assets.Count} 个素材");
        Log.Information(
            "用户通过右键菜单加入描述任务: nodeName={NodeName}, nodeKind={NodeKind}, path={Path}, assetCount={AssetCount}",
            node.DisplayName,
            node.Kind,
            node.FullPath,
            assets.Count);

        await DescribeAssetsAsync(assets);
    }

    public async Task DeleteDescriptionForNodeAsync(AssetLibraryTreeNode? node)
    {
        if (node?.Asset is null)
        {
            Workspace.SetOperatorNotice("请右键具体素材文件，再删除它的描述记录。");
            return;
        }

        Workspace.SelectedAssetTreeNode = node;
        await DeleteDescriptionForAssetAsync(node.Asset);
    }

    private async Task QueueDescriptionsForSelectionAsync()
    {
        var assets = Workspace.GetDescriptionSelectionAssets();
        if (assets.Count == 0)
        {
            Workspace.SetOperatorNotice("当前范围内没有可描述的素材。");
            return;
        }

        await DescribeAssetsAsync(assets);
    }

    private async Task QueueSelectedDescriptionAsync()
    {
        if (Workspace.SelectedAsset is not { } asset)
        {
            Workspace.SetOperatorNotice("请先选择一个素材。");
            return;
        }

        await DescribeAssetsAsync([asset]);
    }

    [ObservableProperty] public partial string RangeStartText { get; set; } = "";

    [ObservableProperty] public partial string RangeEndText { get; set; } = "";

    /// <summary>当前描述按钮是否带时间范围（剪辑素材专用）</summary>
    public bool HasTimeRange
    {
        get => field;
        set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(IsClipRangeVisible));
            }
        }
    }

    /// <summary>当前选中素材是否为剪辑素材（控制「时间范围」开关可见性）</summary>
    public bool IsClipAssetSelected
    {
        get => field;
        set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(IsClipRangeVisible));
            }
        }
    }

    /// <summary>是否显示时间范围输入框（剪辑素材且开启范围描述）</summary>
    public bool IsClipRangeVisible => IsClipAssetSelected && HasTimeRange;

    /// <summary>解析后的开始时间（秒），null=未指定</summary>
    public double? RangeStartSeconds => TryParseTime(RangeStartText, out var value) ? value : null;

    /// <summary>解析后的结束时间（秒），null=未指定</summary>
    public double? RangeEndSeconds => TryParseTime(RangeEndText, out var value) ? value : null;

    private static bool TryParseTime(string? text, out double seconds)
    {
        seconds = 0.0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();
        // 时间码格式 mm:ss 或 hh:mm:ss
        var parts = trimmed.Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length == 2 && double.TryParse(parts[0], out var minutes) && double.TryParse(parts[1], out var secs))
        {
            seconds = (minutes * 60) + secs;
            return true;
        }

        if (parts.Length == 3
            && double.TryParse(parts[0], out var hours)
            && double.TryParse(parts[1], out var mins)
            && double.TryParse(parts[2], out var secondsPart))
        {
            seconds = (hours * 3600) + (mins * 60) + secondsPart;
            return true;
        }

        return double.TryParse(trimmed, out seconds);
    }

    private async Task DescribeAssetsAsync(IReadOnlyList<ManagedAssetRecord> assets)
    {
        if (!BackendStatus.IsBackendReady)
        {
            Workspace.SetOperatorNotice("Python 模型服务尚未就绪，请先等待后端启动完成。");
            return;
        }

        if (DescribeAssetsUseCase is null)
        {
            Workspace.SetOperatorNotice("描述服务未注册，当前无法调用后端。");
            return;
        }

        // 剪辑素材：解析时间范围（只补范围内缺失片段）
        var clipAssets = assets.Where(asset =>
            string.Equals(asset.AssetType, "视频剪辑", StringComparison.Ordinal)).ToArray();
        double? rangeStart = null;
        double? rangeEnd = null;
        if (clipAssets.Length > 0 && HasTimeRange)
        {
            rangeStart = RangeStartSeconds;
            rangeEnd = RangeEndSeconds;
            if (rangeStart is null || rangeEnd is null || rangeEnd.Value <= rangeStart.Value)
            {
                Workspace.SetOperatorNotice("时间范围无效：请输入开始与结束时间（秒或 mm:ss），且结束需大于开始。");
                return;
            }
        }

        try
        {
            var result = await DescribeAssetsUseCase.ExecuteAsync(
                assets,
                BackendStatus.BaseUrl,
                rangeStart: rangeStart,
                rangeEnd: rangeEnd,
                progress: progress =>
                {
                    // UseCase 内部 ConfigureAwait(false) 后回调在后台线程执行，
                    // 统一派发到 UI 线程再刷新工作台状态，避免跨线程改集合。
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (progress.Kind == DescribeAssetProgressKind.Queued)
                        {
                            Workspace.MarkAssetDescriptionQueued(progress.Asset);
                        }
                        else if (progress.Kind == DescribeAssetProgressKind.Completed && progress.Document is not null)
                        {
                            Workspace.CompleteAssetDescription(progress.Asset, progress.Document);
                        }
                        else if (progress.Kind == DescribeAssetProgressKind.Failed && progress.Error is not null)
                        {
                            Workspace.FailAssetDescription(progress.Asset, progress.Error.Message);
                        }
                    });

                    return Task.CompletedTask;
                });

            var rangeSuffix = rangeStart is not null
                ? $"（范围 {rangeStart.Value:0.##}s-{rangeEnd!.Value:0.##}s）"
                : string.Empty;
            Workspace.SetOperatorNotice(
                $"描述任务完成：成功 {result.SuccessCount}，失败 {result.FailureCount}。{rangeSuffix}");
            _activityFeedService.Add(
                $"描述任务完成：成功 {result.SuccessCount}，失败 {result.FailureCount}。{rangeSuffix}");
        }
        catch (OperationCanceledException)
        {
            Workspace.SetOperatorNotice("描述任务已取消。");
            _activityFeedService.Add("描述任务已取消");
        }
        catch (Exception ex)
        {
            Workspace.SetOperatorNotice($"描述任务失败：{ex.Message}");
            _activityFeedService.Add($"描述任务失败：{ex.Message}");
            Log.Error(ex, "批量描述任务失败，assetCount={AssetCount}", assets.Count);
        }
    }

    private async Task DeleteSelectedDescriptionAsync()
    {
        var asset = Workspace.SelectedAsset;
        if (asset is null)
        {
            Workspace.SetOperatorNotice("请先选择一个素材，再删除它的描述记录。");
            return;
        }

        await DeleteDescriptionForAssetAsync(asset);
    }

    private async Task DeleteDescriptionForAssetAsync(ManagedAssetRecord asset)
    {
        if (DeleteAssetDescriptionUseCase is null)
        {
            Workspace.SetOperatorNotice("描述删除服务未注册，当前无法删除描述记录。");
            return;
        }

        try
        {
            var result = await DeleteAssetDescriptionUseCase.ExecuteAsync(asset);
            if (!result.DeletedAny)
            {
                Workspace.SetOperatorNotice($"当前素材没有可删除的描述记录：{asset.Name}");
                _activityFeedService.Add($"描述删除跳过：{asset.Name} 没有记录");
                return;
            }

            Workspace.RemoveAssetDescription(asset, result.VectorDeleted);
            _activityFeedService.Add($"描述删除完成：{asset.Name}");
        }
        catch (Exception ex)
        {
            Workspace.SetOperatorNotice($"删除描述失败：{ex.Message}");
            _activityFeedService.Add($"描述删除失败：{asset.Name} -> {ex.Message}");
            Log.Error(ex, "删除素材描述失败: assetUid={AssetUid}, assetName={AssetName}", asset.AssetUid, asset.Name);
        }
    }
}
