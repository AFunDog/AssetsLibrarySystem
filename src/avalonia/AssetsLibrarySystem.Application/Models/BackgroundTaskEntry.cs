using System.Threading;

namespace AssetsLibrarySystem.Application.Models;

public sealed class BackgroundTaskEntry : ObservableModel
{
    public required string Id { get; init; }

    /// <summary>任务取消令牌源（取消按钮触发 CancelTask 时取消）</summary>
    public CancellationTokenSource Cancellation { get; } = new();

    /// <summary>任务取消令牌</summary>
    public CancellationToken Token => Cancellation.Token;

    public long Sequence { get; set; }

    public string Title
    {
        get => field;
        set => SetProperty(ref field, value);
    } = string.Empty;

    public string StageText
    {
        get => field;
        set => SetProperty(ref field, value);
    } = string.Empty;

    public string DetailText
    {
        get => field;
        set => SetProperty(ref field, value);
    } = string.Empty;

    public string StatusText
    {
        get => field;
        set => SetProperty(ref field, value);
    } = string.Empty;

    public string StartedAtText
    {
        get => field;
        set => SetProperty(ref field, value);
    } = string.Empty;

    public string TimelineText
    {
        get => field;
        set => SetProperty(ref field, value);
    } = string.Empty;

    public bool IsActive
    {
        get => field;
        set => SetProperty(ref field, value);
    }

    /// <summary>进度百分比（0-100），-1 表示不确定</summary>
    public double Progress
    {
        get => field;
        set => SetProperty(ref field, value);
    }

    /// <summary>是否为不确定进度模式（如等待中）</summary>
    public bool IsIndeterminate
    {
        get => field;
        set => SetProperty(ref field, value);
    }
}
