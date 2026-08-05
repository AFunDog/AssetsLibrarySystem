using System;
using System.Collections.Generic;

namespace AssetsLibrarySystem.Application.Models;

/// <summary>一次 API 调用（描述/向量化/检索）的 token 与费用流水条目，追加持久化用于花费审计。</summary>
public sealed record AssetTokenUsageLogEntry(
    long? AssetId,
    string AssetName,
    string AssetType,
    string Mode,
    string Operation,
    string? Model,
    string? Query,
    int InputTokens,
    int OutputTokens,
    int TotalTokens,
    double EstimatedCostCny,
    DateTimeOffset CreatedAt);

/// <summary>指定范围的 token/费用累计统计（流水表 SUM 聚合）。</summary>
public sealed record AssetTokenUsageSummary(
    long CallCount,
    long TotalInputTokens,
    long TotalOutputTokens,
    long TotalTokens,
    double TotalCostCny,
    IReadOnlyList<AssetTokenUsageLogEntry> RecentEntries);
