namespace AssetsLibrarySystem.Avalonia.Services.Settings;

public interface IUserSettingsService
{
    bool AutoWarmupEmbeddingModel { get; set; }

    bool AutoWarmupRerankModel { get; set; }
}
