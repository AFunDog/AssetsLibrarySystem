using AssetsLibrarySystem.Application.Models;
using AssetsLibrarySystem.Application.Infrastructure;
using AssetsLibrarySystem.Application.Services.AssetDescription;
using AssetsLibrarySystem.Application.Services.AssetSearch;
using AssetsLibrarySystem.Application.UseCases.AssetOperations;
using Xunit;
using Microsoft.Extensions.Configuration;

namespace AssetsLibrarySystem.Application.Tests;

public sealed class AssetSearchIndexRefreshUseCaseTests
{
    [Fact]
    public async Task DeleteAssetDescription_ReindexesAfterSuccessfulDeletion()
    {
        var descriptionStore = new FakeDescriptionStore(deleteResult: true);
        var vectorStore = new FakeVectorStore(deleteResult: true);
        var assetSearchService = new FakeAssetSearchService();
        var useCase = new DeleteAssetDescriptionUseCase(descriptionStore, vectorStore, assetSearchService);

        var result = await useCase.ExecuteAsync(CreateAsset());

        Assert.True(result.DeletedAny);
        Assert.Equal(1, assetSearchService.ReindexCallCount);
    }

    [Fact]
    public async Task DeleteAssetDescription_SkipsReindexWhenNothingChanged()
    {
        var descriptionStore = new FakeDescriptionStore(deleteResult: false);
        var vectorStore = new FakeVectorStore(deleteResult: false);
        var assetSearchService = new FakeAssetSearchService();
        var useCase = new DeleteAssetDescriptionUseCase(descriptionStore, vectorStore, assetSearchService);

        var result = await useCase.ExecuteAsync(CreateAsset());

        Assert.False(result.DeletedAny);
        Assert.Equal(0, assetSearchService.ReindexCallCount);
    }

    [Fact]
    public async Task VectorizeDescriptions_ReindexesOnceAfterSuccessfulBatch()
    {
        var asset = CreateAsset();
        var description = CreateDescription(asset);
        var vectorDocument = CreateVectorDocument(asset);
        var descriptionStore = new FakeDescriptionStore(descriptionByAssetId: new Dictionary<string, AssetDescriptionDocument?>
        {
            [asset.AssetUid] = description,
        });
        var vectorStore = new FakeVectorStore();
        var vectorizationService = new FakeTextVectorizationService(new Dictionary<string, IReadOnlyList<AssetDescriptionVectorDocument>>
        {
            [asset.AssetUid] = [vectorDocument],
        });
        var assetSearchService = new FakeAssetSearchService();
        var useCase = new VectorizeDescriptionsUseCase(descriptionStore, vectorStore, vectorizationService, assetSearchService, CreateSearchModelOptionsProvider());

        var result = await useCase.ExecuteAsync([asset], "http://local-backend");

        Assert.Equal(1, result.SuccessCount);
        Assert.Equal(0, result.SkipCount);
        Assert.Equal(0, result.FailureCount);
        Assert.Equal(1, assetSearchService.ReindexCallCount);
        Assert.Single(vectorStore.ReplaceCalls);
    }

    [Fact]
    public async Task VectorizeDescriptions_DoesNotReindexWhenBatchOnlySkips()
    {
        var asset = CreateAsset();
        var description = CreateDescription(asset);
        var freshVector = CreateVectorDocument(asset);
        var descriptionStore = new FakeDescriptionStore(descriptionByAssetId: new Dictionary<string, AssetDescriptionDocument?>
        {
            [asset.AssetUid] = description,
        });
        var vectorStore = new FakeVectorStore(listByAssetId: new Dictionary<string, IReadOnlyList<AssetDescriptionVectorDocument>>
        {
            [asset.AssetUid] = [freshVector],
        }, needsVectorizationResult: false);
        var vectorizationService = new FakeTextVectorizationService();
        var assetSearchService = new FakeAssetSearchService();
        var useCase = new VectorizeDescriptionsUseCase(descriptionStore, vectorStore, vectorizationService, assetSearchService, CreateSearchModelOptionsProvider());

        var result = await useCase.ExecuteAsync([asset], "http://local-backend");

        Assert.Equal(0, result.SuccessCount);
        Assert.Equal(1, result.SkipCount);
        Assert.Equal(0, result.FailureCount);
        Assert.Equal(0, assetSearchService.ReindexCallCount);
        Assert.Empty(vectorStore.ReplaceCalls);
    }

    private static ManagedAssetRecord CreateAsset()
    {
        return new ManagedAssetRecord
        {
            DatabaseId = 1,
            AssetUid = "asset-001",
            Name = "sample.mp3",
            AssetType = "音乐",
            LocalPath = @"D:\Assets\sample.mp3",
            RelativePath = @"music\sample.mp3",
            LibraryName = "默认素材库",
        };
    }

    private static ISearchModelOptionsProvider CreateSearchModelOptionsProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SearchModels:EmbeddingProvider"] = "local",
                ["SearchModels:EmbeddingModel"] = "bge-test",
                ["SearchModels:RerankProvider"] = "local",
                ["SearchModels:RerankModel"] = "rerank-test",
            })
            .Build();
        return new FakeSearchModelOptionsProvider(SearchModelOptions.FromConfiguration(configuration));
    }

    private static AssetDescriptionDocument CreateDescription(ManagedAssetRecord asset)
    {
        return new AssetDescriptionDocument(
            AssetId: asset.DatabaseId,
            AssetUid: asset.AssetUid,
            AssetName: asset.Name,
            AssetType: asset.AssetType,
            CurrentPath: asset.LocalPath,
            Description: """{"全面":"紧张、持续推进的配乐。"}""",
            BackendEndpoint: "http://local-backend",
            Mode: "test",
            GeneratedAt: DateTimeOffset.Parse("2026-06-10T10:00:00+08:00"),
            TokenUsage: null,
            Prompt: null,
            SystemPrompt: null,
            ContentHash: "hash-001",
            MetadataStatus: "ready");
    }

    private static AssetDescriptionVectorDocument CreateVectorDocument(ManagedAssetRecord asset)
    {
        return new AssetDescriptionVectorDocument(
            AssetId: asset.DatabaseId,
            AssetUid: asset.AssetUid,
            AngleType: AssetDescriptionVectorDocument.DefaultAngleType,
            EmbeddingModel: "bge-test",
            VectorDim: 3,
            Vector: [0.1f, 0.2f, 0.3f],
            VectorizedAt: DateTimeOffset.Parse("2026-06-10T10:05:00+08:00"),
            ContentHash: "hash-001");
    }

    private sealed class FakeDescriptionStore : IAssetDescriptionStore
    {
        private IReadOnlyDictionary<string, AssetDescriptionDocument?> DescriptionByAssetId { get; }
        private bool DeleteResult { get; }

        public FakeDescriptionStore(
            bool deleteResult = false,
            IReadOnlyDictionary<string, AssetDescriptionDocument?>? descriptionByAssetId = null)
        {
            DeleteResult = deleteResult;
            DescriptionByAssetId = descriptionByAssetId ?? new Dictionary<string, AssetDescriptionDocument?>();
        }

        public string DatabasePath => "test.db";

        public Task SaveAsync(AssetDescriptionDocument document, CancellationToken ct = default)
        {
            throw new NotSupportedException();
        }

        public Task<AssetDescriptionDocument?> TryGetAsync(long assetId, CancellationToken ct = default)
        {
            return Task.FromResult(DescriptionByAssetId.Values.FirstOrDefault());
        }

        public Task<AssetDescriptionDocument?> TryGetForAssetAsync(ManagedAssetRecord asset, CancellationToken ct = default)
        {
            return Task.FromResult(DescriptionByAssetId.GetValueOrDefault(asset.AssetUid));
        }

        public Task<bool> DeleteAsync(long assetId, CancellationToken ct = default)
        {
            return Task.FromResult(DeleteResult);
        }

        public Task<bool> NeedsVectorizationAsync(
            string assetId,
            string? descriptionContentHash = null,
            DateTimeOffset? descriptionGeneratedAt = null,
            CancellationToken ct = default)
        {
            return Task.FromResult(true);
        }

        public Task MarkAsIndexedAsync(string assetId, CancellationToken ct = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class FakeVectorStore : IAssetDescriptionVectorStore
    {
        private IReadOnlyDictionary<string, IReadOnlyList<AssetDescriptionVectorDocument>> ListByAssetId { get; }
        private bool DeleteResult { get; }
        private bool NeedsVectorizationResult { get; }

        public FakeVectorStore(
            bool deleteResult = false,
            bool needsVectorizationResult = true,
            IReadOnlyDictionary<string, IReadOnlyList<AssetDescriptionVectorDocument>>? listByAssetId = null)
        {
            DeleteResult = deleteResult;
            NeedsVectorizationResult = needsVectorizationResult;
            ListByAssetId = listByAssetId ?? new Dictionary<string, IReadOnlyList<AssetDescriptionVectorDocument>>();
        }

        public string DatabasePath => "test.db";
        public List<(long AssetId, IReadOnlyList<AssetDescriptionVectorDocument> Documents)> ReplaceCalls { get; } = [];

        public Task ReplaceForAssetAsync(long assetId, string embeddingModel, IReadOnlyList<AssetDescriptionVectorDocument> documents, CancellationToken ct = default)
        {
            ReplaceCalls.Add((assetId, documents));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AssetDescriptionVectorDocument>> ListByAssetIdAsync(long assetId, CancellationToken ct = default)
        {
            return Task.FromResult(ListByAssetId.Values.FirstOrDefault() ?? (IReadOnlyList<AssetDescriptionVectorDocument>)[]);
        }

        public Task<bool> DeleteAsync(long assetId, CancellationToken ct = default)
        {
            return Task.FromResult(DeleteResult);
        }

        public Task<bool> NeedsVectorizationAsync(
            long assetId,
            string embeddingModel,
            string? descriptionContentHash = null,
            DateTimeOffset? descriptionGeneratedAt = null,
            CancellationToken ct = default)
        {
            return Task.FromResult(NeedsVectorizationResult);
        }

        public Task MarkAsIndexedAsync(long assetId, CancellationToken ct = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed record FakeSearchModelOptionsProvider(SearchModelOptions Current) : ISearchModelOptionsProvider;

    private sealed class FakeTextVectorizationService : IAssetTextVectorizationService
    {
        private IReadOnlyDictionary<string, IReadOnlyList<AssetDescriptionVectorDocument>> ResultsByAssetId { get; }

        public FakeTextVectorizationService(IReadOnlyDictionary<string, IReadOnlyList<AssetDescriptionVectorDocument>>? resultsByAssetId = null)
        {
            ResultsByAssetId = resultsByAssetId ?? new Dictionary<string, IReadOnlyList<AssetDescriptionVectorDocument>>();
        }

        public Task<IReadOnlyList<AssetDescriptionVectorDocument>> VectorizeAsync(
            AssetDescriptionDocument document,
            string backendBaseUrl,
            string provider,
            string model,
            CancellationToken ct = default)
        {
            if (ResultsByAssetId.TryGetValue(document.AssetUid, out var documents))
            {
                return Task.FromResult(documents);
            }

            throw new InvalidOperationException($"missing vector result for {document.AssetUid}");
        }
    }

    private sealed class FakeAssetSearchService : IAssetSearchService
    {
        public int ReindexCallCount { get; private set; }

        public Task<AssetSearchResponseDocument> SearchAsync(string backendBaseUrl, string query, int candidateTopK = 20, int finalTopK = 5, string? assetFormat = null, CancellationToken ct = default)
        {
            throw new NotSupportedException();
        }

        public Task<AssetReindexResponseDocument> ReindexAsync(CancellationToken ct = default)
        {
            ReindexCallCount++;
            return Task.FromResult(new AssetReindexResponseDocument(0, 0, "test.db", "test.hnsw", "test.meta.json", []));
        }

        public Task<AssetSearchWarmupDocument> WarmupEmbeddingAsync(string backendBaseUrl, CancellationToken ct = default)
        {
            throw new NotSupportedException();
        }

        public Task<AssetSearchWarmupDocument> WarmupRerankAsync(string backendBaseUrl, CancellationToken ct = default)
        {
            throw new NotSupportedException();
        }

        public Task<AssetSearchModelStatusDocument> GetModelStatusAsync(string backendBaseUrl, CancellationToken ct = default)
        {
            throw new NotSupportedException();
        }

        public Task<AssetSearchModelCloseDocument> CloseModelAsync(string backendBaseUrl, string modelKind, CancellationToken ct = default)
        {
            throw new NotSupportedException();
        }
    }
}
