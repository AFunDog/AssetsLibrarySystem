using System;
using System.Threading;
using System.Threading.Tasks;
using AssetsLibrarySystem.Application.Models;

namespace AssetsLibrarySystem.Application.Services.AssetDescription;

public interface IAssetDescriptionService
{
    Task<AssetDescriptionDocument> DescribeAsync(
        ManagedAssetRecord asset,
        string backendBaseUrl,
        string? prompt,
        string? systemPrompt,
        CancellationToken ct = default);

    /// <summary>
    /// 描述剪辑素材（asset_type=视频剪辑）：两阶段（分割落库→逐片段描述）。
    /// rangeStart/rangeEnd 为可选时间范围（秒），只补该范围内缺失片段。
    /// progress：分割阶段进度百分比回调（0-100）。
    /// </summary>
    Task<AssetDescriptionDocument> DescribeClipAsync(
        ManagedAssetRecord asset,
        string backendBaseUrl,
        double? rangeStart,
        double? rangeEnd,
        CancellationToken ct = default,
        Action<int>? progress = null);

    /// <summary>
    /// 仅执行场景分割：把片段时间点骨架写入描述 JSON（不调用 LLM）。
    /// 已有分割结果且（无范围或范围已覆盖）时幂等跳过。
    /// progress：场景检测进度百分比回调（0-100）。
    /// </summary>
    Task<ClipSplitResult> SplitOnlyAsync(
        ManagedAssetRecord asset,
        string backendBaseUrl,
        double? rangeStart,
        double? rangeEnd,
        CancellationToken ct = default,
        Action<int>? progress = null);
}

/// <summary>「仅分割」执行结果</summary>
public sealed record ClipSplitResult(
    AssetDescriptionDocument Document,
    int SegmentCount,
    bool AlreadySplit);
