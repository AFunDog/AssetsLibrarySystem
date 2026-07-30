using System;

namespace AssetsLibrarySystem.Application.Services.BackgroundTasks;

/// <summary>
/// 将后台任务状态变更调度到 UI 线程（或其它同步上下文）。
/// Application 层默认同步执行；Avalonia 注册为 Dispatcher 调度。
/// </summary>
public interface IBackgroundTaskUiScheduler
{
    void Schedule(Action action);
}

/// <summary>默认调度器：在调用线程同步执行。</summary>
public sealed class InlineBackgroundTaskUiScheduler : IBackgroundTaskUiScheduler
{
    public void Schedule(Action action) => action();
}
