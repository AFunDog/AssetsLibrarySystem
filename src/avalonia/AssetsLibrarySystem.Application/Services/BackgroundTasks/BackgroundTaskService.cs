using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using AssetsLibrarySystem.Application.Models;

namespace AssetsLibrarySystem.Application.Services.BackgroundTasks;

public sealed class BackgroundTaskService : ObservableModel, IBackgroundTaskService
{
    private const int MaxCompletedTaskHistory = 20;

    private readonly object _gate = new();
    private IBackgroundTaskUiScheduler UiScheduler { get; }
    private Dictionary<string, BackgroundTaskEntry> TaskIndex { get; } = new(StringComparer.Ordinal);
    private long SequenceCounter { get; set; }

    public BackgroundTaskService(IBackgroundTaskUiScheduler? uiScheduler = null)
    {
        UiScheduler = uiScheduler ?? new InlineBackgroundTaskUiScheduler();
    }

    public ObservableCollection<BackgroundTaskEntry> Tasks { get; } = [];

    public bool HasActiveTaskSummary
    {
        get => field;
        set => SetProperty(ref field, value);
    }

    public string ActiveTaskSummary
    {
        get => field;
        set => SetProperty(ref field, value);
    } = string.Empty;

    public string BeginTask(string title, string stageText, string? detailText = null)
    {
        BackgroundTaskEntry task;
        lock (_gate)
        {
            task = new BackgroundTaskEntry
            {
                Id = Guid.NewGuid().ToString("N"),
                Sequence = ++SequenceCounter,
                Title = title,
                StageText = stageText,
                DetailText = detailText ?? string.Empty,
                StatusText = "执行中",
                StartedAtText = FormatTimestamp(DateTime.Now),
                TimelineText = "刚刚开始",
                IsActive = true
            };
            TaskIndex[task.Id] = task;
        }

        // TaskIndex 已同步写入，保证立即 Update/Complete 能命中；集合变更走 UI 调度。
        ScheduleUi(() =>
        {
            if (!Tasks.Contains(task))
            {
                Tasks.Insert(0, task);
            }

            RefreshSummary();
        });
        return task.Id;
    }

    public void UpdateTask(string taskId, string stageText, string? detailText = null)
    {
        ScheduleUi(() =>
        {
            if (!TryGetTask(taskId, out var task))
            {
                return;
            }

            task.Sequence = NextSequence();
            task.StageText = stageText;
            if (!string.IsNullOrWhiteSpace(detailText))
            {
                task.DetailText = detailText;
            }

            task.StatusText = "执行中";
            task.TimelineText = $"最近更新：{FormatTimestamp(DateTime.Now)}";
            task.IsActive = true;
            EnsureInCollection(task);
            MoveToTop(task);
            RefreshSummary();
        });
    }

    public void UpdateProgress(string taskId, double progress)
    {
        ScheduleUi(() =>
        {
            if (!TryGetTask(taskId, out var task))
            {
                return;
            }

            task.Progress = Math.Clamp(progress, 0, 100);
            task.IsIndeterminate = progress < 0;
            task.StatusText = progress >= 0 ? $"{progress:F0}%" : "进行中";
            task.TimelineText = $"进度：{task.StatusText} · {FormatTimestamp(DateTime.Now)}";
            EnsureInCollection(task);
            MoveToTop(task);
            RefreshSummary();
        });
    }

    public void CompleteTask(string taskId, string? stageText = null, string? detailText = null)
    {
        ScheduleUi(() =>
        {
            if (!TryGetTask(taskId, out var task))
            {
                return;
            }

            task.Sequence = NextSequence();
            if (!string.IsNullOrWhiteSpace(stageText))
            {
                task.StageText = stageText;
            }

            if (!string.IsNullOrWhiteSpace(detailText))
            {
                task.DetailText = detailText;
            }

            var finishedAt = DateTime.Now;
            task.StatusText = "已完成";
            task.TimelineText = $"开始：{task.StartedAtText} · 完成：{FormatTimestamp(finishedAt)}";
            task.IsActive = false;
            task.Progress = 100;
            task.IsIndeterminate = false;
            EnsureInCollection(task);
            MoveToTop(task);
            TrimCompletedTaskHistory();
            RefreshSummary();
        });
    }

    public void FailTask(string taskId, string detailText, string? stageText = null)
    {
        ScheduleUi(() =>
        {
            if (!TryGetTask(taskId, out var task))
            {
                return;
            }

            task.Sequence = NextSequence();
            if (!string.IsNullOrWhiteSpace(stageText))
            {
                task.StageText = stageText;
            }

            task.DetailText = detailText;
            var finishedAt = DateTime.Now;
            task.StatusText = "失败";
            task.TimelineText = $"开始：{task.StartedAtText} · 失败：{FormatTimestamp(finishedAt)}";
            task.IsActive = false;
            task.IsIndeterminate = false;
            EnsureInCollection(task);
            MoveToTop(task);
            TrimCompletedTaskHistory();
            RefreshSummary();
        });
    }

    public void CancelTask(string taskId)
    {
        ScheduleUi(() =>
        {
            if (!TryGetTask(taskId, out var task))
            {
                return;
            }

            // 已完成/失败的任务不再覆盖状态（CancelTask 与 CompleteTask 的 UI 队列竞态防护）
            if (!task.IsActive)
            {
                return;
            }

            task.Cancellation.Cancel();
            task.Sequence = NextSequence();
            task.StageText = "正在取消…";
            task.StatusText = "取消中";
            task.TimelineText = $"请求取消：{FormatTimestamp(DateTime.Now)}";
            EnsureInCollection(task);
            MoveToTop(task);
            RefreshSummary();
        });
    }

    public CancellationToken GetCancellationToken(string taskId)
    {
        lock (_gate)
        {
            return TryGetTask(taskId, out var task) ? task.Token : CancellationToken.None;
        }
    }

    private void ScheduleUi(Action action)
    {
        UiScheduler.Schedule(() =>
        {
            lock (_gate)
            {
                action();
            }
        });
    }

    private bool TryGetTask(string taskId, out BackgroundTaskEntry task)
    {
        return TaskIndex.TryGetValue(taskId, out task!);
    }

    private long NextSequence()
    {
        return ++SequenceCounter;
    }

    private void EnsureInCollection(BackgroundTaskEntry task)
    {
        if (!Tasks.Contains(task))
        {
            Tasks.Insert(0, task);
        }
    }

    private void RefreshSummary()
    {
        var activeTasks = Tasks
            .Where(task => task.IsActive)
            .OrderByDescending(task => task.Sequence)
            .ToList();

        if (activeTasks.Count == 0)
        {
            HasActiveTaskSummary = false;
            ActiveTaskSummary = string.Empty;
            return;
        }

        var currentTask = activeTasks[0];
        HasActiveTaskSummary = true;
        ActiveTaskSummary = activeTasks.Count == 1
            ? currentTask.StageText
            : $"{currentTask.StageText} · 另有 {activeTasks.Count - 1} 个后台任务";
    }

    private void MoveToTop(BackgroundTaskEntry task)
    {
        var index = Tasks.IndexOf(task);
        if (index > 0)
        {
            Tasks.Move(index, 0);
        }
    }

    private void TrimCompletedTaskHistory()
    {
        var completedTasks = Tasks
            .Where(task => !task.IsActive)
            .OrderByDescending(task => task.Sequence)
            .Skip(MaxCompletedTaskHistory)
            .ToList();

        foreach (var task in completedTasks)
        {
            TaskIndex.Remove(task.Id);
            Tasks.Remove(task);
            // 任务已结束并从列表移除，释放取消令牌源避免注册回调长期存活。
            // 注意：释放后任务不再在 TaskIndex 中，GetCancellationToken 只会返回
            // CancellationToken.None，不会再向已释放源注册新回调（会抛 ObjectDisposedException）。
            task.Cancellation.Dispose();
        }
    }

    private static string FormatTimestamp(DateTime value)
    {
        return value.ToString("HH:mm:ss");
    }
}
