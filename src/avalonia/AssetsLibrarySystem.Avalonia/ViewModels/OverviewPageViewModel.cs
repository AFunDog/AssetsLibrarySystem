using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;
using AssetsLibrarySystem.Application.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

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
        Workspace.PropertyChanged += (_, e) => OnPropertyChanged(e.PropertyName);
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
}