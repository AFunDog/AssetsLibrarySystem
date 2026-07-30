using System;

namespace AssetsLibrarySystem.Avalonia.Models;

/// <summary>活动日志条目类型</summary>
public enum ActivityEntryType
{
    Info,
    Success,
    Warning,
    Error
}

/// <summary>结构化的活动日志条目</summary>
public sealed class ActivityFeedEntry
{
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public string Message { get; init; } = string.Empty;
    public ActivityEntryType Type { get; init; } = ActivityEntryType.Info;

    /// <summary>格式化的时间文本</summary>
    public string TimeText => Timestamp.ToString("HH:mm:ss");

    /// <summary>日期分组键</summary>
    public string DateGroupKey => Timestamp.ToString("yyyy-MM-dd");

    /// <summary>图标类型键（用于 UI 绑定）</summary>
    public string IconKind => Type switch
    {
        ActivityEntryType.Success => "CheckCircle",
        ActivityEntryType.Warning => "AlertTriangle",
        ActivityEntryType.Error => "XCircle",
        _ => "Info"
    };

    /// <summary>颜色键</summary>
    public string ColorKey => Type switch
    {
        ActivityEntryType.Success => "StatusDescribedBrush",
        ActivityEntryType.Warning => "StatusProcessingBrush",
        ActivityEntryType.Error => "StatusFailedBrush",
        _ => "AppSecondaryTextBrush"
    };

    public static ActivityFeedEntry CreateInfo(string message) =>
        new() { Message = message, Type = ActivityEntryType.Info };

    public static ActivityFeedEntry CreateSuccess(string message) =>
        new() { Message = message, Type = ActivityEntryType.Success };

    public static ActivityFeedEntry CreateWarning(string message) =>
        new() { Message = message, Type = ActivityEntryType.Warning };

    public static ActivityFeedEntry CreateError(string message) =>
        new() { Message = message, Type = ActivityEntryType.Error };
}