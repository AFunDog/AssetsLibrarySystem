using System.Text.Json;
using AssetsLibrarySystem.Application.Services.BackendApi;
using Xunit;

namespace AssetsLibrarySystem.Application.Tests;

public sealed class BackendApiClientTests
{
    [Fact]
    public async Task BackendSearchClient_UsesCentralSearchEndpoints()
    {
        var transport = new FakeBackendApiTransport
        {
            Response = new BackendSearchIndexResponse(
                "asset-1", "asset.png", "图片", @"D:\asset.png", "description",
                JsonSerializer.SerializeToElement(new[] { 1f, 0f }), 2, "embedding-test", 3),
        };
        var client = new BackendSearchClient(transport);

        await client.IndexAsync(
            "http://backend/",
            new BackendSearchIndexRequest(
                "local", "embedding-test", null, "asset-1", "asset.png", "图片",
                @"D:\asset.png", "description", null));

        Assert.Equal("/api/v1/search/index", transport.RelativePath);
        Assert.Equal("向量化", transport.Operation);
    }

    [Fact]
    public async Task BackendModelClient_UsesCentralModelEndpoints()
    {
        var transport = new FakeBackendApiTransport
        {
            Response = new BackendModelStatusResponse(
                "embedding-test", "rerank-test", "cpu", [], false, false, 0),
        };
        var client = new BackendModelClient(transport);

        await client.GetStatusAsync("http://backend");

        Assert.Equal("/api/v1/search/models/status", transport.RelativePath);
        Assert.Equal("模型状态查询", transport.Operation);
    }

    [Fact]
    public async Task BackendHealthClient_UsesCentralHealthEndpoints()
    {
        var transport = new FakeBackendApiTransport { IsSuccess = true };
        var client = new BackendHealthClient(transport);

        var healthy = await client.IsHealthyAsync("http://backend");

        Assert.True(healthy);
        Assert.Equal("/health", transport.RelativePath);
    }

    private sealed class FakeBackendApiTransport : IBackendApiTransport
    {
        public object? Response { get; init; }
        public bool IsSuccess { get; init; }
        public string? RelativePath { get; private set; }
        public string? Operation { get; private set; }

        public Task<TResponse> GetAsync<TResponse>(
            string backendBaseUrl,
            string relativePath,
            string operation,
            CancellationToken ct)
        {
            Capture(relativePath, operation);
            return Task.FromResult((TResponse)Response!);
        }

        public Task<TResponse> PostAsync<TRequest, TResponse>(
            string backendBaseUrl,
            string relativePath,
            TRequest request,
            string operation,
            CancellationToken ct)
        {
            Capture(relativePath, operation);
            return Task.FromResult((TResponse)Response!);
        }

        public Task PostAsync(string backendBaseUrl, string relativePath, string operation, CancellationToken ct)
        {
            Capture(relativePath, operation);
            return Task.CompletedTask;
        }

        public Task<bool> IsSuccessAsync(string backendBaseUrl, string relativePath, CancellationToken ct)
        {
            RelativePath = relativePath;
            return Task.FromResult(IsSuccess);
        }

        private void Capture(string relativePath, string operation)
        {
            RelativePath = relativePath;
            Operation = operation;
        }
    }
}
