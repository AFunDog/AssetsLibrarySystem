using System;
using AssetsLibrarySystem.Application.Services.AssetSearch;
using Xunit;

namespace AssetsLibrarySystem.Application.Tests;

public sealed class SearchParameterNormalizerTests
{
    [Fact]
    public void Normalize_TrimsQueryAndNormalizesTopKValues()
    {
        var normalizer = new SearchParameterNormalizer();

        var result = normalizer.Normalize("  夜晚背景  ", 10, 20, 5, 0);

        Assert.Equal("夜晚背景", result.Query);
        Assert.Equal(10, result.CandidateTopK);
        Assert.Equal(10, result.FinalTopK);
        Assert.Equal(10, result.ExpandedCandidateTopK);
        Assert.Equal(50, result.RerankTopK);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalize_RejectsEmptyQuery(string query)
    {
        var normalizer = new SearchParameterNormalizer();

        var exception = Assert.Throws<ArgumentException>(
            () => normalizer.Normalize(query, 20, 5, 160, 50));

        Assert.Equal("query", exception.ParamName);
    }
}
