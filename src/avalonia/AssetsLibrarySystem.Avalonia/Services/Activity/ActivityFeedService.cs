using System.Collections.ObjectModel;

namespace AssetsLibrarySystem.Avalonia.Services.Activity;

public sealed class ActivityFeedService
{
    public ActivityFeedService()
    {
        Entries.Add("桌面端作为素材管理主入口，先固定本地工作流边界。");
        Entries.Add("本地素材库目录会持久化为 JSON，素材描述与向量会写入 SQLite，并由 .NET 负责读取展示。");
        Entries.Add("Python 进程仅暴露 HTTP 模型能力，包括描述向量化、召回搜索和索引重建。");
    }

    public ObservableCollection<string> Entries { get; } = [];

    public void Add(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        Entries.Insert(0, message);
    }
}
