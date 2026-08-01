using System;
using System.ComponentModel;

namespace AssetsLibrarySystem.Avalonia.Models;

/// <summary>资源管理器筛选与排序状态</summary>
public sealed class LibraryFilterState : INotifyPropertyChanged
{
    private string _assetTypeFilter = "全部";
    private string _statusFilter = "全部";
    private string _sortBy = "名称";
    private bool _sortAscending = true;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>素材类型筛选：全部 / 文本 / 图片 / 视频 / 音频</summary>
    public string AssetTypeFilter
    {
        get => _assetTypeFilter;
        set
        {
            if (_assetTypeFilter != value)
            {
                _assetTypeFilter = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AssetTypeFilter)));
            }
        }
    }

    /// <summary>状态筛选：全部 / 已描述 / 未描述 / 已向量化 / 待处理</summary>
    public string StatusFilter
    {
        get => _statusFilter;
        set
        {
            if (_statusFilter != value)
            {
                _statusFilter = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusFilter)));
            }
        }
    }

    /// <summary>排序字段：名称 / 类型 / 大小 / 修改时间</summary>
    public string SortBy
    {
        get => _sortBy;
        set
        {
            if (_sortBy != value)
            {
                _sortBy = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SortBy)));
            }
        }
    }

    /// <summary>是否升序排序</summary>
    public bool SortAscending
    {
        get => _sortAscending;
        set
        {
            if (_sortAscending != value)
            {
                _sortAscending = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SortAscending)));
            }
        }
    }

    /// <summary>可选的素材类型值列表</summary>
    public static readonly string[] AssetTypeOptions = ["全部", "文本", "图片", "视频", "音频", "视频剪辑"];

    /// <summary>可选的状态值列表</summary>
    public static readonly string[] StatusOptions = ["全部", "已描述", "未描述", "已向量化", "待处理"];

    /// <summary>可选的排序字段列表</summary>
    public static readonly string[] SortByOptions = ["名称", "类型", "大小"];
}