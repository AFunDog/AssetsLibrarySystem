using System.Collections.ObjectModel;
using AssetsLibrarySystem.Avalonia.Models;
using Avalonia.Threading;

namespace AssetsLibrarySystem.Avalonia.Services.Activity;

/// <summary>
/// 活动日志服务，维护结构化的时间线条目。
/// 最多保留 200 条。
/// 非 UI 线程调用 Add 时会自动派发到 UI 线程，保证 ObservableCollection 安全。
/// </summary>
public sealed class ActivityFeedService
{
    private const int MaxEntries = 200;

    public ActivityFeedService()
    {
        Entries.Add(ActivityFeedEntry.CreateInfo("桌面端作为素材管理主入口，先固定本地工作流边界。"));
        Entries.Add(ActivityFeedEntry.CreateInfo("本地素材库目录会持久化为 SQLite，素材描述与向量也写入 SQLite，并由 .NET 负责读取展示。"));
        Entries.Add(ActivityFeedEntry.CreateInfo("Python 模型网关通过 Python.NET 嵌入进程内调用，负责描述生成、向量化与重排。"));
    }

    /// <summary>结构化的活动条目（最近的在前）</summary>
    public ObservableCollection<ActivityFeedEntry> Entries { get; } = [];

    public void Add(string message)
    {
        Add(ActivityFeedEntry.CreateInfo(message));
    }

    public void Add(ActivityFeedEntry entry)
    {
        if (entry is null || string.IsNullOrWhiteSpace(entry.Message))
            return;

        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => AddCore(entry));
            return;
        }

        AddCore(entry);
    }

    private void AddCore(ActivityFeedEntry entry)
    {
        Entries.Insert(0, entry);

        if (Entries.Count > MaxEntries)
            Entries.RemoveAt(Entries.Count - 1);
    }

    public void AddSuccess(string message) => Add(ActivityFeedEntry.CreateSuccess(message));
    public void AddWarning(string message) => Add(ActivityFeedEntry.CreateWarning(message));
    public void AddError(string message) => Add(ActivityFeedEntry.CreateError(message));
}