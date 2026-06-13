using AssetsLibrarySystem.Application.Infrastructure;

namespace AssetsLibrarySystem.Avalonia.Services.Settings;

public interface IUserSettingsService : ISearchModelOptionsProvider
{
    bool AutoWarmupEmbeddingModel { get; set; }

    bool AutoWarmupRerankModel { get; set; }

    string EmbeddingProvider { get; set; }

    string EmbeddingModel { get; set; }

    string RerankProvider { get; set; }

    string RerankModel { get; set; }
}
