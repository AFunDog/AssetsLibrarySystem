using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AssetsLibrarySystem.Application.Models;
using AssetsLibrarySystem.Application.Services.AssetDescription;
using AssetsLibrarySystem.Application.Services.BackendApi;
using Xunit;

namespace AssetsLibrarySystem.Application.Tests;

public sealed class DescribeClipAsyncTests
{
    private static readonly string TestYamlPath;

    static DescribeClipAsyncTests()
    {
        var baseDir = AppContext.BaseDirectory;
        var current = new DirectoryInfo(baseDir);
        while (current != null)
        {
            var candidate = Path.Combine(current.FullName, "src", "avalonia", "AssetsLibrarySystem.Application", "angle_profiles.yaml");
            if (File.Exists(candidate))
            {
                TestYamlPath = candidate;
                return;
            }
            current = current.Parent;
        }

        TestYamlPath = Path.Combine(baseDir, "angle_profiles.yaml");
    }

    private static ManagedAssetRecord CreateClipAsset()
    {
        var asset = new ManagedAssetRecord
        {
            DatabaseId = 1,
            AssetUid = "clip-uid-1",
            Name = "demo.mp4",
            AssetType = "视频剪辑",
            LocalPath = @"D:\Data\demo.mp4",
            ContentHash = "hash-1",
        };
        return asset;
    }

    private static BackendModelGenerateRequest CaptureRequest(FakeBackendModelClient client, int index) =>
        client.Requests[index];

    private sealed class FakeBackendModelClient : IBackendModelClient
    {
        public List<BackendModelGenerateRequest> Requests { get; } = [];
        public List<string> Responses { get; } = [];
        public bool ForceMock { get; set; }
        private int _callIndex;

        public Task<BackendModelGenerateResponse> GenerateAsync(
            string backendBaseUrl,
            BackendModelGenerateRequest request,
            CancellationToken ct = default)
        {
            Requests.Add(request);
            var index = _callIndex++;
            var output = index < Responses.Count ? Responses[index] : "{}";
            var mode = ForceMock
                ? "mock"
                : request.SlicingOnly ? "slicing" : "live";
            return Task.FromResult(new BackendModelGenerateResponse(
                ProviderSlot: "视频",
                Provider: "dashscope",
                Model: "qwen-vl-max",
                Mode: mode,
                OutputText: output,
                SystemPrompt: "",
                TokenUsage: null));
        }
    }

    private sealed class FakeDescriptionStore : IAssetDescriptionStore
    {
        public string DatabasePath => "test.db";
        public AssetDescriptionDocument? Stored { get; private set; }
        public List<string> SavedDescriptions { get; } = [];

        public Task SaveAsync(AssetDescriptionDocument document, CancellationToken ct = default)
        {
            Stored = document;
            SavedDescriptions.Add(document.Description);
            return Task.CompletedTask;
        }

        public Task<AssetDescriptionDocument?> TryGetAsync(long assetId, CancellationToken ct = default) =>
            Task.FromResult(Stored);

        public Task<AssetDescriptionDocument?> TryGetForAssetAsync(ManagedAssetRecord asset, CancellationToken ct = default) =>
            Task.FromResult(Stored);

        public Task<bool> DeleteAsync(long assetId, CancellationToken ct = default) => Task.FromResult(true);

        public Task UpdateDescriptionAsync(long assetId, string newDescription, CancellationToken ct = default)
        {
            if (Stored is not null)
            {
                Stored = Stored with { Description = newDescription };
            }

            return Task.CompletedTask;
        }
    }

    private static AssetDescriptionService CreateService(
        FakeBackendModelClient client,
        FakeDescriptionStore store) =>
        new(store, client, new AngleProfileManager(TestYamlPath), new SubtypeDetector());

    [Fact]
    public async Task DescribeClipAsync_NoExisting_FirstSplitsThenDescribesAll()
    {
        var client = new FakeBackendModelClient
        {
            Responses =
            {
                // 1. slicing-only 骨架
                """{"整体":{"text":"","tags":[]},"segments":[{"start_time":0.0,"end_time":10.0,"整体":{"text":"","tags":[]}},{"start_time":10.0,"end_time":20.0,"整体":{"text":"","tags":[]}}]}""",
                // 2. 片段描述响应
                """{"整体":{"text":"剪辑总览","tags":["剪辑"]},"segments":[{"start_time":0.0,"end_time":10.0,"整体":{"text":"开场","tags":[]}},{"start_time":10.0,"end_time":20.0,"整体":{"text":"结尾","tags":[]}}]}""",
            },
        };
        var store = new FakeDescriptionStore();
        var service = CreateService(client, store);

        var document = await service.DescribeClipAsync(CreateClipAsset(), "in-process", null, null);

        // 两阶段：先 slicing-only 落库，再按时间点描述
        Assert.Equal(2, client.Requests.Count);
        Assert.True(client.Requests[0].SlicingOnly);
        Assert.False(client.Requests[1].SlicingOnly);
        Assert.Equal(2, client.Requests[1].ExistingSegments!.Length);
        Assert.Equal(0.0, client.Requests[1].ExistingSegments![0].Start);
        Assert.Equal(20.0, client.Requests[1].ExistingSegments![1].End);

        // 最终 JSON：整体 + 已描述片段
        using var doc = JsonDocument.Parse(document.Description);
        Assert.Equal("剪辑总览", doc.RootElement.GetProperty("整体").GetProperty("text").GetString());
        Assert.Equal(2, doc.RootElement.GetProperty("segments").GetArrayLength());
        var missing = StructuredDescriptionHelper.GetMissingSegmentRanges(document.Description);
        Assert.Empty(missing);

        // 分割结果先落库（第一次保存是骨架）
        Assert.Equal(2, store.SavedDescriptions.Count);
        var firstSave = JsonDocument.Parse(store.SavedDescriptions[0]);
        var firstSegment = firstSave.RootElement.GetProperty("segments")[0];
        Assert.Equal("", firstSegment.GetProperty("整体").GetProperty("text").GetString());
    }

    [Fact]
    public async Task DescribeClipAsync_ExistingSkeleton_SkipsSlicingAndOnlyDescribesMissing()
    {
        var store = new FakeDescriptionStore();
        await store.SaveAsync(new AssetDescriptionDocument(
            AssetId: 1,
            AssetUid: "clip-uid-1",
            AssetName: "demo.mp4",
            AssetType: "视频剪辑",
            CurrentPath: @"D:\Data\demo.mp4",
            Description: """{"整体":{"text":"","tags":[]},"segments":[{"start_time":0.0,"end_time":10.0,"整体":{"text":"开场已描述","tags":[]}},{"start_time":10.0,"end_time":20.0,"整体":{"text":"","tags":[]}}]}""",
            BackendEndpoint: "in-process",
            Mode: "slicing",
            GeneratedAt: DateTimeOffset.UtcNow,
            TokenUsage: null,
            Prompt: null,
            SystemPrompt: null,
            ContentHash: "hash-1",
            MetadataStatus: "pending",
            Subtype: "默认"), CancellationToken.None);

        var client = new FakeBackendModelClient
        {
            Responses =
            {
                // 只描述缺失片段 10-20
                """{"整体":{"text":"剪辑总览","tags":[]},"segments":[{"start_time":10.0,"end_time":20.0,"整体":{"text":"结尾已描述","tags":[]}}]}""",
            },
        };
        var service = CreateService(client, store);

        var document = await service.DescribeClipAsync(CreateClipAsset(), "in-process", null, null);

        // 已有分割结果 → 不再 slicing-only，直接描述缺失片段
        Assert.Single(client.Requests);
        Assert.False(client.Requests[0].SlicingOnly);
        var existingSegments = Assert.Single(client.Requests[0].ExistingSegments!);
        Assert.Equal(10.0, existingSegments.Start);
        Assert.Equal(20.0, existingSegments.End);

        // 合并后：片段 0 保留，片段 1 已补
        var missing = StructuredDescriptionHelper.GetMissingSegmentRanges(document.Description);
        Assert.Empty(missing);
        Assert.Equal(2, StructuredDescriptionHelper.GetSegmentCount(document.Description));
    }

    [Fact]
    public async Task DescribeClipAsync_AllSegmentsDescribed_ReturnsWithoutCallingBackend()
    {
        var store = new FakeDescriptionStore();
        var existingJson = """{"整体":{"text":"总览","tags":[]},"segments":[{"start_time":0.0,"end_time":10.0,"整体":{"text":"已描述","tags":[]}}]}""";
        await store.SaveAsync(new AssetDescriptionDocument(
            AssetId: 1,
            AssetUid: "clip-uid-1",
            AssetName: "demo.mp4",
            AssetType: "视频剪辑",
            CurrentPath: @"D:\Data\demo.mp4",
            Description: existingJson,
            BackendEndpoint: "in-process",
            Mode: "live",
            GeneratedAt: DateTimeOffset.UtcNow,
            TokenUsage: null,
            Prompt: null,
            SystemPrompt: null,
            ContentHash: "hash-1",
            MetadataStatus: "ready",
            Subtype: "默认"), CancellationToken.None);

        var client = new FakeBackendModelClient();
        var service = CreateService(client, store);

        var document = await service.DescribeClipAsync(CreateClipAsset(), "in-process", null, null);

        Assert.Empty(client.Requests);
        Assert.Equal(existingJson, document.Description);
    }

    [Fact]
    public async Task DescribeClipAsync_WithRange_OnlyCallsSlicingForUncoveredRange()
    {
        var client = new FakeBackendModelClient
        {
            Responses =
            {
                // 范围内无分割 → slicing-only 补 [30, 50]
                """{"整体":{"text":"","tags":[]},"segments":[{"start_time":30.0,"end_time":40.0,"整体":{"text":"","tags":[]}},{"start_time":40.0,"end_time":50.0,"整体":{"text":"","tags":[]}}]}""",
                // 描述范围内两个缺失片段
                """{"整体":{"text":"新范围摘要","tags":[]},"segments":[{"start_time":30.0,"end_time":40.0,"整体":{"text":"范围A","tags":[]}},{"start_time":40.0,"end_time":50.0,"整体":{"text":"范围B","tags":[]}}]}""",
            },
        };
        var store = new FakeDescriptionStore();
        var service = CreateService(client, store);

        var document = await service.DescribeClipAsync(CreateClipAsset(), "in-process", 30.0, 50.0);

        Assert.Equal(2, client.Requests.Count);
        Assert.True(client.Requests[0].SlicingOnly);
        Assert.Equal(30.0, client.Requests[0].RangeStart);
        Assert.Equal(50.0, client.Requests[0].RangeEnd);
        Assert.False(client.Requests[1].SlicingOnly);

        var missing = StructuredDescriptionHelper.GetMissingSegmentRanges(document.Description);
        Assert.Empty(missing);
        Assert.Equal(2, StructuredDescriptionHelper.GetSegmentCount(document.Description));
        Assert.True(StructuredDescriptionHelper.IsRangeCovered(document.Description, 30.0, 50.0));
    }

    // ===== SplitOnlyAsync（仅场景分割） =====

    [Fact]
    public async Task SplitOnlyAsync_NoExisting_CallsSlicingOnlyAndPersistsSkeleton()
    {
        var client = new FakeBackendModelClient
        {
            Responses =
            {
                """{"整体":{"text":"","tags":[]},"segments":[{"start_time":0.0,"end_time":10.0,"整体":{"text":"","tags":[]}},{"start_time":10.0,"end_time":20.0,"整体":{"text":"","tags":[]}}]}""",
            },
        };
        var store = new FakeDescriptionStore();
        var service = CreateService(client, store);

        var result = await service.SplitOnlyAsync(CreateClipAsset(), "in-process", null, null);

        Assert.False(result.AlreadySplit);
        Assert.Equal(2, result.SegmentCount);
        Assert.Single(client.Requests);
        Assert.True(client.Requests[0].SlicingOnly);
        Assert.Null(client.Requests[0].RangeStart);
        // 骨架已落库（保存了一次）
        Assert.Single(store.SavedDescriptions);
        var missing = StructuredDescriptionHelper.GetMissingSegmentRanges(result.Document.Description);
        Assert.Equal(2, missing.Count); // 全部未描述
    }

    [Fact]
    public async Task SplitOnlyAsync_AlreadySplit_SkipsWithoutCallingBackend()
    {
        var store = new FakeDescriptionStore();
        var existingJson = """{"整体":{"text":"","tags":[]},"segments":[{"start_time":0.0,"end_time":10.0,"整体":{"text":"开场已描述","tags":[]}},{"start_time":10.0,"end_time":20.0,"整体":{"text":"","tags":[]}}]}""";
        await store.SaveAsync(new AssetDescriptionDocument(
            AssetId: 1,
            AssetUid: "clip-uid-1",
            AssetName: "demo.mp4",
            AssetType: "视频剪辑",
            CurrentPath: @"D:\Data\demo.mp4",
            Description: existingJson,
            BackendEndpoint: "in-process",
            Mode: "slicing",
            GeneratedAt: DateTimeOffset.UtcNow,
            TokenUsage: null,
            Prompt: null,
            SystemPrompt: null,
            ContentHash: "hash-1",
            MetadataStatus: "pending",
            Subtype: "默认"), CancellationToken.None);

        var client = new FakeBackendModelClient();
        var service = CreateService(client, store);

        var result = await service.SplitOnlyAsync(CreateClipAsset(), "in-process", null, null);

        Assert.True(result.AlreadySplit);
        Assert.Equal(2, result.SegmentCount);
        Assert.Empty(client.Requests);
        Assert.Single(store.SavedDescriptions); // 仅 seed 时保存过一次，幂等跳过不新增
    }

    [Fact]
    public async Task SplitOnlyAsync_WithUncoveredRange_SplitsSubrangeAndKeepsDescribedSegments()    {
        var store = new FakeDescriptionStore();
        var existingJson = """{"整体":{"text":"总览","tags":[]},"segments":[{"start_time":0.0,"end_time":10.0,"整体":{"text":"已描述","tags":[]}}]}""";
        await store.SaveAsync(new AssetDescriptionDocument(
            AssetId: 1,
            AssetUid: "clip-uid-1",
            AssetName: "demo.mp4",
            AssetType: "视频剪辑",
            CurrentPath: @"D:\Data\demo.mp4",
            Description: existingJson,
            BackendEndpoint: "in-process",
            Mode: "live",
            GeneratedAt: DateTimeOffset.UtcNow,
            TokenUsage: null,
            Prompt: null,
            SystemPrompt: null,
            ContentHash: "hash-1",
            MetadataStatus: "ready",
            Subtype: "默认"), CancellationToken.None);

        var client = new FakeBackendModelClient
        {
            Responses =
            {
                """{"整体":{"text":"","tags":[]},"segments":[{"start_time":30.0,"end_time":40.0,"整体":{"text":"","tags":[]}}]}""",
            },
        };
        var service = CreateService(client, store);

        var result = await service.SplitOnlyAsync(CreateClipAsset(), "in-process", 30.0, 50.0);

        Assert.False(result.AlreadySplit);
        Assert.Equal(2, result.SegmentCount); // 原有 1 + 新增 1
        var splitRequest = Assert.Single(client.Requests);
        Assert.True(splitRequest.SlicingOnly);
        Assert.Equal(30.0, splitRequest.RangeStart);
        Assert.Equal(50.0, splitRequest.RangeEnd);
        // 已描述片段保留（文本仍在），新增片段为骨架
        var missing = StructuredDescriptionHelper.GetMissingSegmentRanges(result.Document.Description);
        Assert.Single(missing);
        Assert.Equal(30.0, missing[0].Start);
    }

    [Fact]
    public async Task SplitOnlyAsync_MockMode_ThrowsWithApiKeyHint()
    {
        // 未配置 API Key 时后端返回 mock，分割无意义 → 明确抛错提示配置
        var client = new FakeBackendModelClient
        {
            ForceMock = true,
            Responses =
            {
                "当前处于桌面端联调阶段，返回占位响应。",
            },
        };
        var store = new FakeDescriptionStore();
        var service = CreateService(client, store);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SplitOnlyAsync(CreateClipAsset(), "in-process", null, null));

        Assert.Contains("API Key", ex.Message);
        Assert.Contains("mock", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(store.SavedDescriptions); // mock 骨架不落库
    }
}
