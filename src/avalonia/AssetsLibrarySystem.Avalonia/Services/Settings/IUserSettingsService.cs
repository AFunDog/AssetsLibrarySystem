using AssetsLibrarySystem.Application.Infrastructure;

namespace AssetsLibrarySystem.Avalonia.Services.Settings;

public interface IUserSettingsService : ISearchModelOptionsProvider
{
    bool AutoWarmupEmbeddingModel { get; set; }

    bool AutoWarmupRerankModel { get; set; }

    string EmbeddingProvider { get; set; }

    string EmbeddingModel { get; set; }

    int EmbeddingDimensions { get; set; }

    string RerankProvider { get; set; }

    string RerankModel { get; set; }

    int SearchCandidateTopK { get; set; }

    int SearchExpandedCandidateTopK { get; set; }

    int SearchRerankTopK { get; set; }

    int SearchFinalTopK { get; set; }
}
