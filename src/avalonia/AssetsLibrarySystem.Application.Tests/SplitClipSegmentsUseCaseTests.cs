using System.Collections.ObjectModel;
using System.ComponentModel;
using AssetsLibrarySystem.Application.Models;
using AssetsLibrarySystem.Application.Services.AssetDescription;
using AssetsLibrarySystem.Application.Services.BackgroundTasks;
using AssetsLibrarySystem.Application.UseCases.AssetOperations;
using Xunit;

namespace AssetsLibrarySystem.Application.Tests;

/// <summary>
/// SplitClipSegmentsUseCase：已分割幂等跳过 / 新分割成功 / 异常计数 / 取消传播。
/// </summary>
public sealed class SplitClipSegmentsUseCaseTests
{
    private static ManagedAssetRecord TestAsset => new()
    {
        DatabaseId = 1,
        AssetUid = "asset-1",
        Name = "01.mkv",
        AssetType = "视频剪辑",
        RelativePath = "01.mkv",
        LocalPath = @"C:\videos\01.mkv",
        FileSize = 1024,
        ModifiedTimeUtc = DateTime.UtcNow,
    };

    [Fact]
    public async Task ExecuteAsync_AlreadySplit_SkipsAndCompletes()
    {
        var service = new FakeDescriptionService(splitResult: new ClipSplitResult(Document(1), 5, true));
        var background = new FakeBackgroundTaskService();
        var useCase = new SplitClipSegmentsUseCase(service, background);

        var result = await useCase.ExecuteAsync([TestAsset], "http://backend");

        Assert.Equal(0, result.SuccessCount);
        Assert.Equal(1, result.SkipCount);
        Assert.Equal(0, result.FailureCount);
        Assert.Equal(1, service.SplitCalls);
        Assert.Single(background.Completed);
    }

    [Fact]
    public async Task ExecuteAsync_NewSplit_Succeeds()
    {
        var service = new FakeDescriptionService(splitResult: new ClipSplitResult(Document(12), 12, false));
        var background = new FakeBackgroundTaskService();
        var useCase = new SplitClipSegmentsUseCase(service, background);

        var result = await useCase.ExecuteAsync([TestAsset], "http://backend");

        Assert.Equal(1, result.SuccessCount);
        Assert.Equal(0, result.SkipCount);
        Assert.Equal(0, result.FailureCount);
        Assert.Single(background.Completed);
    }

    [Fact]
    public async Task ExecuteAsync_Failure_CountsAndFailsTask()
    {
        var service = new FakeDescriptionService(error: new InvalidOperationException("后端不可用"));
        var background = new FakeBackgroundTaskService();
        var useCase = new SplitClipSegmentsUseCase(service, background);

        var result = await useCase.ExecuteAsync([TestAsset], "http://backend");

        Assert.Equal(0, result.SuccessCount);
        Assert.Equal(1, result.FailureCount);
        Assert.Single(background.Failed);
    }

    [Fact]
    public async Task ExecuteAsync_Cancellation_Rejects()
    {
        var service = new FakeDescriptionService(error: new OperationCanceledException());
        var background = new FakeBackgroundTaskService();
        var useCase = new SplitClipSegmentsUseCase(service, background);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => useCase.ExecuteAsync([TestAsset], "http://backend"));
    }

    private static AssetDescriptionDocument Document(int segmentCount)
    {
        var segments = Enumerable.Range(0, segmentCount)
            .Select(i => new Dictionary<string, object?>
            {
                ["start_time"] = i * 10.0,
                ["end_time"] = (i + 1) * 10.0,
                ["整体"] = new Dictionary<string, object?> { ["text"] = $"片段{i}", ["tags"] = new List<string>() },
            })
            .ToList();
        return new AssetDescriptionDocument(
            AssetId: 1,
            AssetUid: "asset-1",
            AssetName: "01.mkv",
            AssetType: "视频剪辑",
            CurrentPath: @"C:\videos\01.mkv",
            Description: System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["整体"] = new Dictionary<string, object?> { ["text"] = "总览", ["tags"] = new List<string>() },
                ["segments"] = segments,
            }),
            BackendEndpoint: "http://backend",
            Mode: "live",
            GeneratedAt: DateTimeOffset.UtcNow,
            TokenUsage: null,
            Prompt: null,
            SystemPrompt: null,
            ContentHash: "hash",
            MetadataStatus: "none");
    }

    private sealed class FakeDescriptionService(
        ClipSplitResult? splitResult = null,
        Exception? error = null) : IAssetDescriptionService
    {
        public int SplitCalls { get; private set; }

        public Task<AssetDescriptionDocument> DescribeAsync(
            ManagedAssetRecord asset,
            string backendBaseUrl,
            string? prompt,
            string? systemPrompt,
            CancellationToken ct = default)
        {
            return Task.FromResult(Document(0));
        }

        public Task<AssetDescriptionDocument> DescribeClipAsync(
            ManagedAssetRecord asset,
            string backendBaseUrl,
            double? rangeStart,
            double? rangeEnd,
            CancellationToken ct = default,
            Action<int>? progress = null)
        {
            return Task.FromResult(Document(0));
        }

        public Task<ClipSplitResult> SplitOnlyAsync(
            ManagedAssetRecord asset,
            string backendBaseUrl,
            double? rangeStart,
            double? rangeEnd,
            CancellationToken ct = default,
            Action<int>? progress = null)
        {
            SplitCalls++;
            if (error is not null)
            {
                throw error;
            }

            return Task.FromResult(splitResult!);
        }
    }

    private sealed class FakeBackgroundTaskService : IBackgroundTaskService
    {
        public ObservableCollection<BackgroundTaskEntry> Tasks { get; } = [];
        public bool HasActiveTaskSummary => false;
        public string ActiveTaskSummary => "测试任务";
        public List<string> Completed { get; } = [];
        public List<string> Failed { get; } = [];

        public event PropertyChangedEventHandler? PropertyChanged;

        public string BeginTask(string title, string stageText, string? detailText = null)
        {
            var id = Guid.NewGuid().ToString("N");
            Tasks.Add(new BackgroundTaskEntry { Id = id, Title = title });
            return id;
        }

        public void UpdateTask(string taskId, string stageText, string? detailText = null)
        {
        }

        public void UpdateProgress(string taskId, double progress)
        {
        }

        public CancellationToken GetCancellationToken(string taskId) => CancellationToken.None;

        public void CancelTask(string taskId)
        {
        }

        public void CompleteTask(string taskId, string? stageText = null, string? detailText = null)
        {
            Completed.Add(taskId);
        }

        public void FailTask(string taskId, string detailText, string? stageText = null)
        {
            Failed.Add(taskId);
        }
    }
}
