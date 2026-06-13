using Microsoft.Extensions.Configuration;

namespace AssetsLibrarySystem.Application.Infrastructure;

public sealed record SearchModelOptions(
    string EmbeddingProvider,
    string EmbeddingModel,
    int EmbeddingDimensions,
    string RerankProvider,
    string RerankModel)
{
    private const string DashScopeProvider = "dashscope";
    private const string LocalProvider = "local";
    private const string DefaultDashScopeEmbeddingModel = "text-embedding-v4";
    private const string DefaultLocalEmbeddingModel = "Qwen/Qwen3-Embedding-0.6B";
    private const string DefaultDashScopeRerankModel = "qwen3-rerank";
    private const string DefaultLocalRerankModel = "Qwen/Qwen3-Reranker-0.6B";

    public string EmbeddingModelKey => IsDashScopeEmbeddingProvider
        ? FormatEmbeddingModelKey(EmbeddingModel, EmbeddingDimensions)
        : EmbeddingModel;

    public bool IsDashScopeEmbeddingProvider => string.Equals(EmbeddingProvider, DashScopeProvider, System.StringComparison.OrdinalIgnoreCase);

    public static SearchModelOptions FromConfiguration(IConfiguration configuration)
    {
        var embeddingProvider = NormalizeProvider(configuration["SearchModels:EmbeddingProvider"]);
        var rerankProvider = NormalizeProvider(configuration["SearchModels:RerankProvider"]);

        return new SearchModelOptions(
            embeddingProvider,
            ReadEmbeddingModel(configuration, embeddingProvider),
            ReadEmbeddingDimensions(configuration, embeddingProvider),
            rerankProvider,
            ReadRerankModel(configuration, rerankProvider));
    }

    public static int NormalizeEmbeddingDimensions(int? value)
    {
        return value is 2048 or 1024 or 512 ? value.Value : 1024;
    }

    public static string FormatEmbeddingModelKey(string model, int dimensions)
    {
        var normalizedModel = string.IsNullOrWhiteSpace(model) ? DefaultDashScopeEmbeddingModel : model.Trim();
        return $"{normalizedModel}@{NormalizeEmbeddingDimensions(dimensions)}d";
    }

    private static string ReadEmbeddingModel(IConfiguration configuration, string provider)
    {
        var providerSection = GetProviderSectionName(provider);
        var defaultModel = provider == LocalProvider ? DefaultLocalEmbeddingModel : DefaultDashScopeEmbeddingModel;
        return NormalizeModel(
            configuration[$"SearchModels:EmbeddingModels:{providerSection}:Model"] ??
            configuration[$"SearchModels:Providers:{providerSection}:EmbeddingModel"] ??
            (provider == NormalizeProvider(configuration["SearchModels:EmbeddingProvider"]) ? configuration["SearchModels:EmbeddingModel"] : null),
            defaultModel);
    }

    private static int ReadEmbeddingDimensions(IConfiguration configuration, string provider)
    {
        var providerSection = GetProviderSectionName(provider);
        return NormalizeEmbeddingDimensions(
            configuration.GetValue<int?>($"SearchModels:EmbeddingModels:{providerSection}:Dimensions") ??
            configuration.GetValue<int?>($"SearchModels:Providers:{providerSection}:EmbeddingDimensions") ??
            (provider == NormalizeProvider(configuration["SearchModels:EmbeddingProvider"])
                ? configuration.GetValue<int?>("SearchModels:EmbeddingDimensions")
                : null));
    }

    private static string ReadRerankModel(IConfiguration configuration, string provider)
    {
        var providerSection = GetProviderSectionName(provider);
        var defaultModel = provider == LocalProvider ? DefaultLocalRerankModel : DefaultDashScopeRerankModel;
        return NormalizeModel(
            configuration[$"SearchModels:RerankModels:{providerSection}:Model"] ??
            configuration[$"SearchModels:Providers:{providerSection}:RerankModel"] ??
            (provider == NormalizeProvider(configuration["SearchModels:RerankProvider"]) ? configuration["SearchModels:RerankModel"] : null),
            defaultModel);
    }

    private static string NormalizeProvider(string? value) =>
        string.Equals(value?.Trim(), LocalProvider, System.StringComparison.OrdinalIgnoreCase) ? LocalProvider : DashScopeProvider;

    private static string NormalizeModel(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string GetProviderSectionName(string provider) =>
        provider == LocalProvider ? "Local" : "DashScope";
}

public interface ISearchModelOptionsProvider
{
    SearchModelOptions Current { get; }
}

public sealed class ConfigurationSearchModelOptionsProvider(IConfiguration configuration) : ISearchModelOptionsProvider
{
    public SearchModelOptions Current => SearchModelOptions.FromConfiguration(configuration);
}
