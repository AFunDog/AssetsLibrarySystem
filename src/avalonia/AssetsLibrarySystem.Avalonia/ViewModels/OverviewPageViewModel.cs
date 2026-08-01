using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using AssetsLibrarySystem.Application.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace AssetsLibrarySystem.Avalonia.ViewModels;

public sealed class OverviewPageViewModel : ObservableObject
{
    private BackendStatusViewModel BackendStatus { get; }
    private LibraryWorkspaceViewModel Workspace { get; }

    public OverviewPageViewModel(
        BackendStatusViewModel backendStatus,
        LibraryWorkspaceViewModel workspace)
    {
        BackendStatus = backendStatus;
        Workspace = workspace;
        RefreshWorkspaceCommand = new AsyncRelayCommand(() => Workspace.ScanSelectedLibraryAsync());

        BackendStatus.PropertyChanged += (_, e) => OnPropertyChanged(e.PropertyName);
        Workspace.PropertyChanged += (_, e) =>
        {
            OnPropertyChanged(e.PropertyName);
            UpdateChartData();
        };
    }

    [Obsolete("仅供设计器使用")]
    public OverviewPageViewModel()
        : this(new BackendStatusViewModel(), new LibraryWorkspaceViewModel())
    {
    }

    // ===== 后端状态（委托给 BackendStatusViewModel） =====
    public string BackendStatusTitle => BackendStatus.BackendStatusTitle;
    public string BackendStatusStage => BackendStatus.BackendStatusStage;
    public string BackendStatusDetail => BackendStatus.BackendStatusDetail;
    public string BackendEndpoint => BackendStatus.BackendEndpoint;

    // ===== 工作台状态（委托给 LibraryWorkspaceViewModel） =====
    public ObservableCollection<DashboardMetric> Metrics => Workspace.Metrics;
    public string WorkspaceTitle => Workspace.WorkspaceTitle;
    public string WorkspaceSummary => Workspace.WorkspaceSummary;
    public string AssetSummary => Workspace.AssetSummary;
    public string OperatorNotice => Workspace.OperatorNotice;

    public IAsyncRelayCommand RefreshWorkspaceCommand { get; }

    // ===== 图表数据 =====

    /// <summary>素材类型分布饼图系列</summary>
    public ObservableCollection<ISeries> AssetTypeSeries { get; } = [];

    /// <summary>处理进度柱状图系列</summary>
    public ObservableCollection<ISeries> ProgressSeries { get; } = [];

    /// <summary>饼图颜色</summary>
    private static readonly SolidColorPaint[] PieColors =
    [
        new SolidColorPaint(SKColor.Parse("#4C94FF")), // 文本 - 蓝色
        new SolidColorPaint(SKColor.Parse("#52C41A")), // 图片 - 绿色
        new SolidColorPaint(SKColor.Parse("#FA8C16")), // 视频 - 橙色
        new SolidColorPaint(SKColor.Parse("#FF4D4F")), // 音频 - 红色
        new SolidColorPaint(SKColor.Parse("#B37FEB")), // 视频剪辑 - 紫色
    ];

    /// <summary>柱状图颜色</summary>
    private static readonly SolidColorPaint BarDescribedPaint = new(SKColor.Parse("#52C41A"));
    private static readonly SolidColorPaint BarVectorizedPaint = new(SKColor.Parse("#4C94FF"));
    private static readonly SolidColorPaint BarPendingPaint = new(SKColor.Parse("#B8B8B8"));

    /// <summary>更新图表数据（由 Workspace 的 PropertyChanged 触发）</summary>
    private void UpdateChartData()
    {
        UpdateAssetTypePieChart();
        UpdateProgressBarChart();
    }

    private void UpdateAssetTypePieChart()
    {
        AssetTypeSeries.Clear();
        var typeColors = new Dictionary<string, int>
        {
            ["文本"] = 0, ["图片"] = 1, ["视频"] = 2, ["音频"] = 3, ["视频剪辑"] = 4
        };

        foreach (var metric in Metrics)
        {
            if (!typeColors.TryGetValue(metric.Label, out var colorIndex))
                continue;

            if (int.TryParse(metric.Value, out var count) && count > 0)
            {
                AssetTypeSeries.Add(new PieSeries<int>
                {
                    Values = [count],
                    Name = metric.Label,
                    Fill = PieColors[colorIndex],
                    Stroke = null,
                    DataLabelsSize = 12,
                    DataLabelsPosition = LiveChartsCore.Measure.PolarLabelsPosition.Outer,
                    HoverPushout = 4,
                });
            }
        }
    }

    private void UpdateProgressBarChart()
    {
        ProgressSeries.Clear();

        // 从 Metrics 中提取已描述/已向量化/待描述数据
        var described = Metrics.FirstOrDefault(m => m.Label == "已描述");
        var vectorized = Metrics.FirstOrDefault(m => m.Label == "已向量化");
        var pending = Metrics.FirstOrDefault(m => m.Label == "待描述");

        if (described is null || vectorized is null || pending is null)
            return;

        int.TryParse(described.Value, out var describedCount);
        int.TryParse(vectorized.Value, out var vectorizedCount);
        int.TryParse(pending.Value, out var pendingCount);

        ProgressSeries.Add(new ColumnSeries<int>
        {
            Values = [describedCount],
            Name = "已描述",
            Fill = BarDescribedPaint,
            Stroke = null,
            MaxBarWidth = 30,
        });

        ProgressSeries.Add(new ColumnSeries<int>
        {
            Values = [vectorizedCount],
            Name = "已向量化",
            Fill = BarVectorizedPaint,
            Stroke = null,
            MaxBarWidth = 30,
        });

        ProgressSeries.Add(new ColumnSeries<int>
        {
            Values = [pendingCount],
            Name = "待处理",
            Fill = BarPendingPaint,
            Stroke = null,
            MaxBarWidth = 30,
        });
    }

    }