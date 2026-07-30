using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using Serilog;

namespace AssetsLibrarySystem.Avalonia.Services.Search;

/// <summary>
/// 搜索历史记录服务。
/// 保存最近搜索的查询词，支持去重、过滤建议和 JSON 持久化。
/// </summary>
public sealed class SearchHistoryService
{
    private const int MaxEntries = 20;
    private const string HistoryFileName = "search-history.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _historyPath;
    private readonly List<string> _history = [];

    /// <summary>最近的搜索历史（最近的在最前）</summary>
    public IReadOnlyList<string> History => _history.AsReadOnly();

    public SearchHistoryService()
        : this(ResolveHistoryPath())
    {
    }

    public SearchHistoryService(string historyPath)
    {
        _historyPath = historyPath;
        Load();
        Log.Debug("SearchHistoryService 已创建: historyPath={HistoryPath}, entryCount={Count}", _historyPath, _history.Count);
    }

    /// <summary>添加一条搜索记录，去重后移到最前</summary>
    public void AddQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return;

        var trimmed = query.Trim();
        _history.Remove(trimmed);
        _history.Insert(0, trimmed);

        if (_history.Count > MaxEntries)
            _history.RemoveAt(_history.Count - 1);

        Save();
        Log.Debug("搜索历史已添加: query={Query}, totalEntries={Count}", trimmed, _history.Count);
    }

    /// <summary>清空所有搜索历史</summary>
    public void ClearHistory()
    {
        _history.Clear();
        Save();
        Log.Debug("搜索历史已清空");
    }

    /// <summary>根据输入文本获取匹配的历史建议（最多 10 条）</summary>
    public IReadOnlyList<string> GetSuggestions(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return [];

        var trimmed = input.Trim();
        if (trimmed.Length == 0)
            return [];

        // 模糊匹配：包含输入文本的历史记录
        return _history
            .Where(h => h.Contains(trimmed, StringComparison.OrdinalIgnoreCase))
            .Take(10)
            .ToList();
    }

    /// <summary>删除单条历史记录</summary>
    public void RemoveQuery(string query)
    {
        if (_history.Remove(query))
        {
            Save();
            Log.Debug("搜索历史已删除: query={Query}", query);
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_historyPath))
                return;

            var json = File.ReadAllText(_historyPath);
            var entries = JsonSerializer.Deserialize<List<string>>(json, JsonOptions);
            if (entries is null || entries.Count == 0)
                return;

            _history.Clear();
            foreach (var entry in entries)
            {
                if (!string.IsNullOrWhiteSpace(entry))
                    _history.Add(entry);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "加载搜索历史失败: path={Path}", _historyPath);
        }
    }

    private void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_historyPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(_history, JsonOptions);
            File.WriteAllText(_historyPath, json);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "保存搜索历史失败: path={Path}", _historyPath);
        }
    }

    private static string ResolveHistoryPath()
    {
        // 优先使用 data 目录
        var dataRoot = AppDomain.CurrentDomain.GetData("DataRoot") as string;
        if (!string.IsNullOrWhiteSpace(dataRoot))
            return Path.Combine(dataRoot, HistoryFileName);

        // 回退到当前目录下的 data 子目录
        var baseDir = AppContext.BaseDirectory;
        return Path.Combine(baseDir, "data", HistoryFileName);
    }
}