using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using AssetsLibrarySystem.Application.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AssetsLibrarySystem.Application.Services.BackgroundTasks;

public sealed partial class BackgroundTaskService : ObservableObject, IBackgroundTaskService
{
    private const int MaxCompletedTaskHistory = 20;

    private Dictionary<string, BackgroundTaskEntry> TaskIndex { get; } = new(StringComparer.Ordinal);
    private long SequenceCounter { get; set; }

    public ObservableCollection<BackgroundTaskEntry> Tasks { get; } = [];

    [ObservableProperty]
    public partial bool HasActiveTaskSummary { get; set; }

    [ObservableProperty]
    public partial string ActiveTaskSummary { get; set; } = string.Empty;

    public string BeginTask(string title, string stageText, string? detailText = null)
    {
        var task = new BackgroundTaskEntry
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
        Tasks.Insert(0, task);
        RefreshSummary();
        return task.Id;
    }

    public void UpdateTask(string taskId, string stageText, string? detailText = null)
    {
        if (!TaskIndex.TryGetValue(taskId, out var task))
        {
            return;
        }

        task.Sequence = ++SequenceCounter;
        task.StageText = stageText;
        if (!string.IsNullOrWhiteSpace(detailText))
        {
            task.DetailText = detailText;
        }

        task.StatusText = "执行中";
        task.TimelineText = $"最近更新：{FormatTimestamp(DateTime.Now)}";
        task.IsActive = true;
        MoveToTop(task);
        RefreshSummary();
    }

    public void CompleteTask(string taskId, string? stageText = null, string? detailText = null)
    {
        if (!TaskIndex.TryGetValue(taskId, out var task))
        {
            return;
        }

        task.Sequence = ++SequenceCounter;
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
        MoveToTop(task);
        TrimCompletedTaskHistory();
        RefreshSummary();
    }

    public void FailTask(string taskId, string detailText, string? stageText = null)
    {
        if (!TaskIndex.TryGetValue(taskId, out var task))
        {
            return;
        }

        task.Sequence = ++SequenceCounter;
        if (!string.IsNullOrWhiteSpace(stageText))
        {
            task.StageText = stageText;
        }

        task.DetailText = detailText;
        var finishedAt = DateTime.Now;
        task.StatusText = "失败";
        task.TimelineText = $"开始：{task.StartedAtText} · 失败：{FormatTimestamp(finishedAt)}";
        task.IsActive = false;
        MoveToTop(task);
        TrimCompletedTaskHistory();
        RefreshSummary();
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
        }
    }

    private static string FormatTimestamp(DateTime value)
    {
        return value.ToString("HH:mm:ss");
    }
}
