using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections.ObjectModel;

namespace AssetsLibrarySystem.Application.Models;

public sealed class AssetSearchDocument
{
    public AssetSearchDocument()
    {
    }

    public AssetSearchDocument(
        string assetUid,
        string assetName,
        string assetType,
        string currentPath,
        string description,
        DateTimeOffset? generatedAt,
        float? embeddingSimilarity,
        float? vectorDistance,
        float rerankScore,
        IEnumerable<string>? tags = null,
        IEnumerable<string>? angleTags = null)
    {
        AssetUid = assetUid;
        AssetName = assetName;
        AssetType = assetType;
        CurrentPath = currentPath;
        Description = description;
        GeneratedAt = generatedAt;
        EmbeddingSimilarity = embeddingSimilarity;
        VectorDistance = vectorDistance;
        RerankScore = rerankScore;
        Tags = tags?.ToArray() ?? [];
        AngleTags = angleTags?.ToArray() ?? [];
    }

    public string AssetUid { get; set; } = string.Empty;
    public string AssetId
    {
        get => AssetUid;
        set => AssetUid = value;
    }
    public string AssetName { get; set; } = string.Empty;
    public string AssetType { get; set; } = string.Empty;
    public string CurrentPath { get; set; } = string.Empty;
    public string AssetPath
    {
        get => CurrentPath;
        set => CurrentPath = value;
    }
    public string Description { get; set; } = string.Empty;
    public DateTimeOffset? GeneratedAt { get; set; }
    public float? EmbeddingSimilarity { get; set; }
    public float? VectorDistance { get; set; }
    public float RerankScore { get; set; }
    public float? CombinedScore { get; set; }
    public string[] Tags { get; set; } = [];
    public string[] AngleTags { get; set; } = [];

    /// <summary>命中片段序号（片段级检索结果；null=整体/素材级结果）</summary>
    public int? SegmentIndex { get; set; }

    /// <summary>命中片段开始时间（秒）</summary>
    public double? StartTime { get; set; }

    /// <summary>命中片段结束时间（秒）</summary>
    public double? EndTime { get; set; }

    /// <summary>是否为片段级检索结果</summary>
    public bool IsSegmentResult => SegmentIndex is not null;

    /// <summary>片段时间范围展示文案，如 "[00:30-01:15]"；非片段返回空</summary>
    public string SegmentTimeLabel => StartTime is null || EndTime is null
        ? string.Empty
        : $"[{FormatTime(StartTime.Value)}-{FormatTime(EndTime.Value)}]";

    private static string FormatTime(double seconds)
    {
        var total = Math.Max(0, (int)Math.Round(seconds));
        var hours = total / 3600;
        var minutes = (total % 3600) / 60;
        var secs = total % 60;
        return hours > 0
            ? $"{hours:00}:{minutes:00}:{secs:00}"
            : $"{minutes:00}:{secs:00}";
    }

    /// <summary>搜索匹配高亮分段（由 ViewModel 在搜索完成后设置）</summary>
    public IReadOnlyList<HighlightSegment> HighlightedDescription { get; set; } = [];
}

public sealed record AssetSearchResponseDocument(
    string Query,
    int CandidateTopK,
    int FinalTopK,
    string? AssetFormat,
    string AssetFormatMode,
    string EmbeddingModel,
    string RerankModel,
    string SearchStrategy,
    int TotalVectorRecordCount,
    int FilteredVectorRecordCount,
    int ExpandedCandidateTopK,
    int VectorCandidateCount,
    int RerankCandidateCount,
    int ReturnedCount,
    double ElapsedMs,
    int? EmbeddingTokenUsage,
    int? RerankTokenUsage,
    int? TotalTokenUsage,
    AssetSearchDocument[] Results);

public sealed record AssetReindexResponseDocument(
    int DocumentCount,
    int VectorDim,
    string DatabasePath,
    string IndexPath,
    string MetadataPath,
    string[] EmbeddingModels);
