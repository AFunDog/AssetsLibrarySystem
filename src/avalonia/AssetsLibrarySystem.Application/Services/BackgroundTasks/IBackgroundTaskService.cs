using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading;
using AssetsLibrarySystem.Application.Models;

namespace AssetsLibrarySystem.Application.Services.BackgroundTasks;

public interface IBackgroundTaskService : INotifyPropertyChanged
{
    ObservableCollection<BackgroundTaskEntry> Tasks { get; }

    bool HasActiveTaskSummary { get; }

    string ActiveTaskSummary { get; }

    string BeginTask(string title, string stageText, string? detailText = null);

    void UpdateTask(string taskId, string stageText, string? detailText = null);

    /// <summary>更新任务进度百分比（0-100）</summary>
    void UpdateProgress(string taskId, double progress);

    /// <summary>获取任务关联的取消令牌（任务不存在时返回 CancellationToken.None）</summary>
    CancellationToken GetCancellationToken(string taskId);

    /// <summary>请求取消指定任务（触发其取消令牌）</summary>
    void CancelTask(string taskId);

    void CompleteTask(string taskId, string? stageText = null, string? detailText = null);

    void FailTask(string taskId, string detailText, string? stageText = null);
}
