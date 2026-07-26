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
    private const string DefaultDashScopeEmbeddingModel = "text-embedding-v4";
    private const string DefaultDashScopeRerankModel = "qwen3-rerank";

    public string EmbeddingModelKey => FormatEmbeddingModelKey(EmbeddingModel, EmbeddingDimensions);

    public static SearchModelOptions FromConfiguration(IConfiguration configuration)
    {
        return new SearchModelOptions(
            DashScopeProvider,
            ReadEmbeddingModel(configuration),
            ReadEmbeddingDimensions(configuration),
            DashScopeProvider,
            ReadRerankModel(configuration));
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

    private static string ReadEmbeddingModel(IConfiguration configuration)
    {
        return NormalizeModel(
            configuration["SearchModels:EmbeddingModel"] ??
            configuration["SearchModels:EmbeddingModels:DashScope:Model"] ??
            configuration["SearchModels:Providers:DashScope:EmbeddingModel"],
            DefaultDashScopeEmbeddingModel);
    }

    private static int ReadEmbeddingDimensions(IConfiguration configuration)
    {
        return NormalizeEmbeddingDimensions(
            configuration.GetValue<int?>("SearchModels:EmbeddingDimensions") ??
            configuration.GetValue<int?>($"SearchModels:EmbeddingModels:DashScope:Dimensions") ??
            configuration.GetValue<int?>($"SearchModels:Providers:DashScope:EmbeddingDimensions"));
    }

    private static string ReadRerankModel(IConfiguration configuration)
    {
        return NormalizeModel(
            configuration["SearchModels:RerankModel"] ??
            configuration["SearchModels:RerankModels:DashScope:Model"] ??
            configuration["SearchModels:Providers:DashScope:RerankModel"],
            DefaultDashScopeRerankModel);
    }

    private static string NormalizeModel(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}

public interface ISearchModelOptionsProvider
{
    SearchModelOptions Current { get; }
}

public sealed class ConfigurationSearchModelOptionsProvider(IConfiguration configuration) : ISearchModelOptionsProvider
{
    public SearchModelOptions Current => SearchModelOptions.FromConfiguration(configuration);
}