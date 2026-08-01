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
    /// </summary>
    Task<AssetDescriptionDocument> DescribeClipAsync(
        ManagedAssetRecord asset,
        string backendBaseUrl,
        double? rangeStart,
        double? rangeEnd,
        CancellationToken ct = default);
}
