using AssetsLibrarySystem.Application.Infrastructure;
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
        Assert.Equal("image-1", Assert.Single(response.Results).AssetUid);
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
            GeneratedAt: DateTimeOffset.UtcNow,
            VectorizedAt: DateTimeOffset.UtcNow,
            EmbeddingModel: "embedding-test",
            Vector: vector);

    private sealed class FakeSearchModelOptionsProvider : ISearchModelOptionsProvider
    {
        public SearchModelOptions Current { get; } =
            new("local", "embedding-test", 1024, "local", "rerank-test");
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
