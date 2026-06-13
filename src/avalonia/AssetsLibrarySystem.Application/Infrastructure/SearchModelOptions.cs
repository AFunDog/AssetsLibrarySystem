using Microsoft.Extensions.Configuration;

namespace AssetsLibrarySystem.Application.Infrastructure;

public sealed record SearchModelOptions(
    string EmbeddingProvider,
    string EmbeddingModel,
    int EmbeddingDimensions,
    string RerankProvider,
    string RerankModel)
{
    public string EmbeddingModelKey => IsDashScopeEmbeddingProvider
        ? FormatEmbeddingModelKey(EmbeddingModel, EmbeddingDimensions)
        : EmbeddingModel;

    public bool IsDashScopeEmbeddingProvider => string.Equals(EmbeddingProvider, "dashscope", System.StringComparison.OrdinalIgnoreCase);

    public static SearchModelOptions FromConfiguration(IConfiguration configuration)
    {
        return new SearchModelOptions(
            configuration["SearchModels:EmbeddingProvider"] ?? "dashscope",
            configuration["SearchModels:EmbeddingModel"] ?? "text-embedding-v4",
            NormalizeEmbeddingDimensions(configuration.GetValue<int?>("SearchModels:EmbeddingDimensions")),
            configuration["SearchModels:RerankProvider"] ?? "dashscope",
            configuration["SearchModels:RerankModel"] ?? "qwen3-rerank");
    }

    public static int NormalizeEmbeddingDimensions(int? value)
    {
        return value is 2048 or 1024 or 512 ? value.Value : 1024;
    }

    public static string FormatEmbeddingModelKey(string model, int dimensions)
    {
        var normalizedModel = string.IsNullOrWhiteSpace(model) ? "text-embedding-v4" : model.Trim();
        return $"{normalizedModel}@{NormalizeEmbeddingDimensions(dimensions)}d";
    }
}

public interface ISearchModelOptionsProvider
{
    SearchModelOptions Current { get; }
}

public sealed class ConfigurationSearchModelOptionsProvider(IConfiguration configuration) : ISearchModelOptionsProvider
{
    public SearchModelOptions Current => SearchModelOptions.FromConfiguration(configuration);
}
