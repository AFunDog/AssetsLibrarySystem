using AssetsLibrarySystem.Application.Infrastructure;
using AssetsLibrarySystem.Application.Models;
using AssetsLibrarySystem.Application.Services.AssetSearch;
using Xunit;

namespace AssetsLibrarySystem.Application.Tests;

public sealed class AssetSearchPipelineTests
{
    [Fact]
    public async Task ExecuteAsync_RunsSearchStagesAndBuildsDiagnostics()
    {
        var records = new[]
        {
            CreateRecord("image-1", "全面", "图片", [1f, 0f]),
            CreateRecord("image-1", "视觉", "图片", [0.9f, 0.1f]),
            CreateRecord("audio-1", "全面", "音频", [0f, 1f]),
        };
        var pipeline = new AssetSearchPipeline(
            new FakeSearchModelOptionsProvider(),
            new SearchParameterNormalizer(),
            new AssetFormatResolver(),
            new FakeVectorRecordRepository(records),
            new FakeQueryEmbeddingClient([1f, 0f]),
            new VectorRetrieverSelector(new ExactVectorRetriever(), new HnswVectorRetriever()),
            new RerankCandidateSelector(),
            new FakeRerankClient(),
            new ScoreFusionService(),
            new SearchResultAggregator());

        var response = await pipeline.ExecuteAsync(
            new AssetSearchPipelineRequest("http://backend", "  夜晚图片  ", 20, 5, "智能类型", 160, 50));

        Assert.Equal("夜晚图片", response.Query);
        Assert.Equal("smart", response.AssetFormatMode);
        Assert.Equal("图片", response.AssetFormat);
        Assert.Equal("ExactCosine", response.SearchStrategy);
        Assert.Equal(3, response.TotalVectorRecordCount);
        Assert.Equal(2, response.FilteredVectorRecordCount);
        Assert.Equal(2, response.VectorCandidateCount);
        Assert.Equal(2, response.RerankCandidateCount);
        Assert.Equal(1, response.ReturnedCount);
        Assert.Equal(7, response.EmbeddingTokenUsage);
        Assert.Equal(11, response.RerankTokenUsage);
        Assert.Equal(18, response.TotalTokenUsage);
        var result = Assert.Single(response.Results);
        Assert.Equal("image-1", result.AssetUid);
        Assert.Equal(["风格：电子氛围", "情感：紧张"], result.AngleTags);
    }

    [Fact]
    public async Task VectorRetrieverSelector_UsesExactRetrieverForSmallRecordSet()
    {
        var selector = new VectorRetrieverSelector(new ExactVectorRetriever(), new HnswVectorRetriever());

        var result = await selector.RetrieveAsync(
            "embedding-test",
            [CreateRecord("asset-1", "全面", "图片", [1f, 0f])],
            [1f, 0f],
            10);

        Assert.Equal("ExactCosine", result.SearchStrategy);
        Assert.Equal(1, result.EffectiveExpandedCandidateTopK);
        Assert.Equal("asset-1::全面", Assert.Single(result.Candidates).CandidateId);
    }

    [Fact]
    public void ExtractAngleTags_ReturnsAllNonComprehensiveDescriptionAngles()
    {
        const string description = """{"全面":"整体描述","乐器":"钢琴与弦乐","风格":{"text":"电影配乐"},"情感":"紧张"}""";

        var tags = StructuredDescriptionHelper.ExtractAngleTags(description);

        Assert.Equal(["乐器：钢琴与弦乐", "风格：电影配乐", "情感：紧张"], tags);
    }

    [Fact]
    public async Task Aggregate_ReturnsEachHitClipSegmentAsIndependentResult()
    {
        // 剪辑素材：两个片段各自命中（每个片段有整体+场景两个角度），普通素材命中一条
        var records = new[]
        {
            CreateSegmentRecord("clip-1", 0, "整体", 0.0, 10.0, [1f, 0f]),
            CreateSegmentRecord("clip-1", 0, "场景", 0.0, 10.0, [0.95f, 0.05f]),
            CreateSegmentRecord("clip-1", 1, "整体", 10.0, 25.0, [0.9f, 0.1f]),
            CreateSegmentRecord("clip-1", 1, "场景", 10.0, 25.0, [0.85f, 0.15f]),
            CreateRecord("image-1", "整体", "图片", [0.8f, 0.2f]),
        };
        var pipeline = new AssetSearchPipeline(
            new FakeSearchModelOptionsProvider(),
            new SearchParameterNormalizer(),
            new AssetFormatResolver(),
            new FakeVectorRecordRepository(records),
            new FakeQueryEmbeddingClient([1f, 0f]),
            new VectorRetrieverSelector(new ExactVectorRetriever(), new HnswVectorRetriever()),
            new RerankCandidateSelector(),
            new FakeRerankClient(),
            new ScoreFusionService(),
            new SearchResultAggregator());

        var response = await pipeline.ExecuteAsync(
            new AssetSearchPipelineRequest("http://backend", "片段", 20, 5, null, 160, 50));

        // 片段 0、片段 1、图片素材各一条 → 共 3 条独立结果
        Assert.Equal(3, response.ReturnedCount);
        var clipResults = response.Results.Where(r => r.AssetUid == "clip-1").ToArray();
        Assert.Equal(2, clipResults.Length);
        var segmentIndices = clipResults.Select(r => r.SegmentIndex).OrderBy(i => i).ToArray();
        Assert.Equal([0, 1], segmentIndices);
        // 每个片段结果携带时间范围
        var seg0 = clipResults.First(r => r.SegmentIndex == 0);
        Assert.Equal(0.0, seg0.StartTime);
        Assert.Equal(10.0, seg0.EndTime);
        Assert.True(seg0.IsSegmentResult);
        // 普通素材结果无片段标记
        var image = Assert.Single(response.Results, r => r.AssetUid == "image-1");
        Assert.False(image.IsSegmentResult);
        Assert.Null(image.SegmentIndex);
    }

    private static LocalVectorRecord CreateSegmentRecord(
        string assetUid,
        int segmentIndex,
        string angleType,
        double startTime,
        double endTime,
        float[] vector) =>
        new(
            AssetUid: assetUid,
            AngleType: SegmentAngleType.Build(segmentIndex, angleType),
            AssetName: $"{assetUid}.mp4",
            AssetType: "视频剪辑",
            AssetPath: $@"D:\Assets\{assetUid}.mp4",
            PrimaryDescription: $"{assetUid} 整体摘要",
            SegmentText: $"{assetUid} seg{segmentIndex} {angleType}",
            Tags: [],
            AngleTags: [],
            GeneratedAt: DateTimeOffset.UtcNow,
            VectorizedAt: DateTimeOffset.UtcNow,
            EmbeddingModel: "embedding-test",
            Vector: vector,
            SegmentIndex: segmentIndex,
            StartTime: startTime,
            EndTime: endTime);

    private static LocalVectorRecord CreateRecord(
        string assetUid,
        string angleType,
        string assetType,
        float[] vector) =>
        new(
            AssetUid: assetUid,
            AngleType: angleType,
            AssetName: $"{assetUid}.asset",
            AssetType: assetType,
            AssetPath: $@"D:\Assets\{assetUid}.asset",
            PrimaryDescription: $"{assetUid} description",
            SegmentText: $"{assetUid} {angleType}",
            Tags: [],
            AngleTags: ["风格：电子氛围", "情感：紧张"],
            GeneratedAt: DateTimeOffset.UtcNow,
            VectorizedAt: DateTimeOffset.UtcNow,
            EmbeddingModel: "embedding-test",
            Vector: vector);

    private sealed class FakeSearchModelOptionsProvider : ISearchModelOptionsProvider
    {
        public SearchModelOptions Current { get; } =
            new("dashscope", "embedding-test", 1024, "dashscope", "rerank-test");
    }

    private sealed class FakeVectorRecordRepository(IReadOnlyList<LocalVectorRecord> records)
        : IVectorRecordRepository
    {
        public Task<IReadOnlyList<LocalVectorRecord>> LoadAsync(
            string embeddingModel,
            CancellationToken ct = default) =>
            Task.FromResult(records);
    }

    private sealed class FakeQueryEmbeddingClient(float[] vector) : IQueryEmbeddingClient
    {
        public Task<QueryEmbeddingResult> EmbedQueryAsync(
            string backendBaseUrl,
            string text,
            SearchModelOptions searchModels,
            CancellationToken ct = default) =>
            Task.FromResult(new QueryEmbeddingResult(vector, searchModels.EmbeddingModel, 7));
    }

    private sealed class FakeRerankClient : IRerankClient
    {
        public Task<RerankResult> RerankAsync(
            string backendBaseUrl,
            string query,
            IReadOnlyList<VectorCandidateRecord> candidates,
            int rerankTopK,
            SearchModelOptions searchModels,
            CancellationToken ct = default)
        {
            var scores = candidates
                .Select((candidate, index) => new SearchRerankScore(candidate.CandidateId, candidates.Count - index))
                .ToArray();
            return Task.FromResult(new RerankResult(searchModels.RerankModel, scores, 11));
        }
    }
}
