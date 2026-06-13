using Microsoft.Extensions.Configuration;

namespace AssetsLibrarySystem.Application.Infrastructure;

public sealed record SearchModelOptions(
    string EmbeddingProvider,
    string EmbeddingModel,
    string RerankProvider,
    string RerankModel)
{
    public static SearchModelOptions FromConfiguration(IConfiguration configuration)
    {
        return new SearchModelOptions(
            configuration["SearchModels:EmbeddingProvider"] ?? "dashscope",
            configuration["SearchModels:EmbeddingModel"] ?? "text-embedding-v4",
            configuration["SearchModels:RerankProvider"] ?? "dashscope",
            configuration["SearchModels:RerankModel"] ?? "qwen3-rerank");
    }
}
