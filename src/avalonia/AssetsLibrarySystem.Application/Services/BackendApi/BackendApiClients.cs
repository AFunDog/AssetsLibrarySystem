using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace AssetsLibrarySystem.Application.Services.BackendApi;

public sealed class BackendApiException : InvalidOperationException
{
    public BackendApiException(string operation, HttpStatusCode statusCode, string responseBody)
        : base($"后端{operation}失败（HTTP {(int)statusCode}）：{responseBody}")
    {
        Operation = operation;
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    public BackendApiException(string operation, Exception innerException)
        : base($"后端{operation}请求失败：{innerException.Message}", innerException)
    {
        Operation = operation;
    }

    public string Operation { get; }
    public HttpStatusCode? StatusCode { get; }
    public string ResponseBody { get; } = string.Empty;
}

public interface IBackendSearchClient
{
    Task<BackendSearchIndexResponse> IndexAsync(
        string backendBaseUrl,
        BackendSearchIndexRequest request,
        CancellationToken ct = default);

    Task<BackendSearchQueryResponse> RerankAsync(
        string backendBaseUrl,
        BackendSearchQueryRequest request,
        CancellationToken ct = default);
}

public interface IBackendModelClient
{
    Task<BackendModelGenerateResponse> GenerateAsync(
        string backendBaseUrl,
        BackendModelGenerateRequest request,
        CancellationToken ct = default);

    Task<BackendModelWarmupResponse> WarmupAsync(
        string backendBaseUrl,
        string modelKind,
        CancellationToken ct = default);

    Task<BackendModelStatusResponse> GetStatusAsync(
        string backendBaseUrl,
        CancellationToken ct = default);

    Task<BackendModelCloseResponse> CloseAsync(
        string backendBaseUrl,
        BackendModelCloseRequest request,
        CancellationToken ct = default);
}

public interface IBackendHealthClient
{
    Task<bool> IsHealthyAsync(string backendBaseUrl, CancellationToken ct = default);

    Task SendHeartbeatAsync(string backendBaseUrl, CancellationToken ct = default);
}

public interface IBackendApiTransport
{
    Task<TResponse> GetAsync<TResponse>(
        string backendBaseUrl,
        string relativePath,
        string operation,
        CancellationToken ct);

    Task<TResponse> PostAsync<TRequest, TResponse>(
        string backendBaseUrl,
        string relativePath,
        TRequest request,
        string operation,
        CancellationToken ct);

    Task PostAsync(string backendBaseUrl, string relativePath, string operation, CancellationToken ct);

    Task<bool> IsSuccessAsync(string backendBaseUrl, string relativePath, CancellationToken ct);
}

public sealed class BackendApiTransport : IBackendApiTransport, IDisposable
{
    private HttpClient Http { get; } = new();
    private JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public async Task<TResponse> GetAsync<TResponse>(
        string backendBaseUrl,
        string relativePath,
        string operation,
        CancellationToken ct)
    {
        try
        {
            using var response = await Http.GetAsync(BuildEndpoint(backendBaseUrl, relativePath), ct).ConfigureAwait(false);
            return await ReadResponseAsync<TResponse>(response, operation, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ShouldWrap(ex, ct))
        {
            throw new BackendApiException(operation, ex);
        }
    }

    public async Task<TResponse> PostAsync<TRequest, TResponse>(
        string backendBaseUrl,
        string relativePath,
        TRequest request,
        string operation,
        CancellationToken ct)
    {
        try
        {
            using var content = CreateJsonContent(request);
            using var response = await Http
                .PostAsync(BuildEndpoint(backendBaseUrl, relativePath), content, ct)
                .ConfigureAwait(false);
            return await ReadResponseAsync<TResponse>(response, operation, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ShouldWrap(ex, ct))
        {
            throw new BackendApiException(operation, ex);
        }
    }

    public async Task PostAsync(
        string backendBaseUrl,
        string relativePath,
        string operation,
        CancellationToken ct)
    {
        try
        {
            using var content = new StringContent("{}", Encoding.UTF8, "application/json");
            using var response = await Http
                .PostAsync(BuildEndpoint(backendBaseUrl, relativePath), content, ct)
                .ConfigureAwait(false);
            await EnsureSuccessAsync(response, operation, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ShouldWrap(ex, ct))
        {
            throw new BackendApiException(operation, ex);
        }
    }

    public async Task<bool> IsSuccessAsync(
        string backendBaseUrl,
        string relativePath,
        CancellationToken ct)
    {
        try
        {
            using var response = await Http.GetAsync(BuildEndpoint(backendBaseUrl, relativePath), ct).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return false;
        }
    }

    public void Dispose() => Http.Dispose();

    private async Task<TResponse> ReadResponseAsync<TResponse>(
        HttpResponseMessage response,
        string operation,
        CancellationToken ct)
    {
        var responseBody = await EnsureSuccessAsync(response, operation, ct).ConfigureAwait(false);
        try
        {
            return JsonSerializer.Deserialize<TResponse>(responseBody, JsonOptions)
                ?? throw new InvalidOperationException($"后端{operation}返回空响应。");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"后端{operation}响应格式无效。", ex);
        }
    }

    private static async Task<string> EnsureSuccessAsync(
        HttpResponseMessage response,
        string operation,
        CancellationToken ct)
    {
        var responseBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
        {
            return responseBody;
        }

        Log.Warning(
            "后端 API 请求失败: operation={Operation}, statusCode={StatusCode}, body={Body}",
            operation,
            (int)response.StatusCode,
            responseBody);
        throw new BackendApiException(operation, response.StatusCode, responseBody);
    }

    private StringContent CreateJsonContent<TRequest>(TRequest request) =>
        new(JsonSerializer.Serialize(request, JsonOptions), Encoding.UTF8, "application/json");

    private static bool ShouldWrap(Exception ex, CancellationToken ct) =>
        ex is not BackendApiException
        && (ex is HttpRequestException || ex is TaskCanceledException && !ct.IsCancellationRequested);

    private static string BuildEndpoint(string backendBaseUrl, string relativePath) =>
        $"{backendBaseUrl.TrimEnd('/')}/{relativePath.TrimStart('/')}";
}

public sealed class BackendSearchClient : IBackendSearchClient
{
    private IBackendApiTransport Transport { get; }

    public BackendSearchClient(IBackendApiTransport transport)
    {
        Transport = transport;
    }

    public Task<BackendSearchIndexResponse> IndexAsync(
        string backendBaseUrl,
        BackendSearchIndexRequest request,
        CancellationToken ct = default) =>
        Transport.PostAsync<BackendSearchIndexRequest, BackendSearchIndexResponse>(
            backendBaseUrl, "/api/v1/search/index", request, "向量化", ct);

    public Task<BackendSearchQueryResponse> RerankAsync(
        string backendBaseUrl,
        BackendSearchQueryRequest request,
        CancellationToken ct = default) =>
        Transport.PostAsync<BackendSearchQueryRequest, BackendSearchQueryResponse>(
            backendBaseUrl, "/api/v1/search/query", request, "重排序", ct);
}

public sealed class BackendModelClient : IBackendModelClient
{
    private IBackendApiTransport Transport { get; }

    public BackendModelClient(IBackendApiTransport transport)
    {
        Transport = transport;
    }

    public Task<BackendModelGenerateResponse> GenerateAsync(
        string backendBaseUrl,
        BackendModelGenerateRequest request,
        CancellationToken ct = default) =>
        Transport.PostAsync<BackendModelGenerateRequest, BackendModelGenerateResponse>(
            backendBaseUrl, "/api/v1/model/generate", request, "描述生成", ct);

    public Task<BackendModelWarmupResponse> WarmupAsync(
        string backendBaseUrl,
        string modelKind,
        CancellationToken ct = default) =>
        Transport.PostAsync<object, BackendModelWarmupResponse>(
            backendBaseUrl, $"/api/v1/search/warmup/{modelKind}", new { }, "模型预热", ct);

    public Task<BackendModelStatusResponse> GetStatusAsync(
        string backendBaseUrl,
        CancellationToken ct = default) =>
        Transport.GetAsync<BackendModelStatusResponse>(
            backendBaseUrl, "/api/v1/search/models/status", "模型状态查询", ct);

    public Task<BackendModelCloseResponse> CloseAsync(
        string backendBaseUrl,
        BackendModelCloseRequest request,
        CancellationToken ct = default) =>
        Transport.PostAsync<BackendModelCloseRequest, BackendModelCloseResponse>(
            backendBaseUrl, "/api/v1/search/models/close", request, "模型关闭", ct);
}

public sealed class BackendHealthClient : IBackendHealthClient
{
    private static TimeSpan RequestTimeout { get; } = TimeSpan.FromSeconds(5);
    private IBackendApiTransport Transport { get; }

    public BackendHealthClient(IBackendApiTransport transport)
    {
        Transport = transport;
    }

    public async Task<bool> IsHealthyAsync(string backendBaseUrl, CancellationToken ct = default)
    {
        using var timeout = new CancellationTokenSource(RequestTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        try
        {
            return await Transport.IsSuccessAsync(backendBaseUrl, "/health", linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return false;
        }
    }

    public async Task SendHeartbeatAsync(string backendBaseUrl, CancellationToken ct = default)
    {
        using var timeout = new CancellationTokenSource(RequestTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        await Transport.PostAsync(backendBaseUrl, "/internal/heartbeat", "心跳", linked.Token).ConfigureAwait(false);
    }
}
