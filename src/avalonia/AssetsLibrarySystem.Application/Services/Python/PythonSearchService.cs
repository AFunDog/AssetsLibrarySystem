using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AssetsLibrarySystem.Application.Services.BackendApi;
using Python.Runtime;
using Serilog;

namespace AssetsLibrarySystem.Application.Services.Python;

public sealed class PythonSearchService : IBackendSearchClient
{
    private PythonEngineService Engine { get; }

    public PythonSearchService(PythonEngineService engine)
    {
        Engine = engine;
    }

    public Task<BackendSearchIndexResponse> IndexAsync(
        string backendBaseUrl,
        BackendSearchIndexRequest request,
        CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            return Engine.Execute<BackendSearchIndexResponse>(() =>
            {
                Log.Information(
                    "PythonSearchService 调用 vectorize: model={Model}, asset={Asset}",
                    request.Model, request.AssetName);

                try
                {
                    dynamic searchService = GetSearchService();
                    var pyRequest = BuildIndexRequest(request);
                    dynamic pyResponse = searchService.vectorize(pyRequest);
                    return ConvertIndexResponse(pyResponse);
                }
                catch (PythonException ex)
                {
                    Log.Error("Python vectorize 调用失败: {Traceback}", ex.StackTrace);
                    throw new InvalidOperationException(
                        $"Python 向量化服务调用失败：{ex.Message}", ex);
                }
            });
        }, ct);
    }

    public Task<BackendSearchQueryResponse> RerankAsync(
        string backendBaseUrl,
        BackendSearchQueryRequest request,
        CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            return Engine.Execute<BackendSearchQueryResponse>(() =>
            {
                Log.Information(
                    "PythonSearchService 调用 rerank: model={Model}, query_len={QueryLen}, candidates={Count}",
                    request.Model, request.Query.Length, request.Candidates.Length);

                try
                {
                    dynamic searchService = GetSearchService();
                    var pyRequest = BuildQueryRequest(request);
                    dynamic pyResponse = searchService.rerank(pyRequest);
                    return ConvertQueryResponse(pyResponse);
                }
                catch (PythonException ex)
                {
                    Log.Error("Python rerank 调用失败: {Traceback}", ex.StackTrace);
                    throw new InvalidOperationException(
                        $"Python 重排序服务调用失败：{ex.Message}", ex);
                }
            });
        }, ct);
    }

    private static dynamic GetSearchService()
    {
        dynamic app = Py.Import("app.application.services.search_service");
        return app.SearchService();
    }

    private static PyObject BuildIndexRequest(BackendSearchIndexRequest request)
    {
        dynamic schemas = Py.Import("app.schemas.search");
        var kw = new PyDict();
        kw["provider"] = new PyString(request.Provider);
        kw["model"] = new PyString(request.Model);
        kw["asset_id"] = new PyString(request.AssetId);
        kw["asset_name"] = new PyString(request.AssetName);
        kw["asset_format"] = new PyString(request.AssetFormat);
        kw["asset_path"] = new PyString(request.AssetPath);
        kw["description"] = new PyString(request.Description);
        kw["embedding_dimensions"] = request.EmbeddingDimensions.HasValue
            ? new PyInt(request.EmbeddingDimensions.Value)
            : Runtime.None;
        kw["generated_at"] = Runtime.None;
        return schemas.SearchIndexRequest.Invoke(Array.Empty<PyObject>(), kw);
    }

    private static PyObject BuildQueryRequest(BackendSearchQueryRequest request)
    {
        dynamic schemas = Py.Import("app.schemas.search");

        var pyCandidates = new PyList();
        foreach (var c in request.Candidates)
        {
            var cKw = new PyDict();
            cKw["candidate_id"] = c.CandidateId is not null ? new PyString(c.CandidateId) : Runtime.None;
            cKw["asset_id"] = new PyString(c.AssetId);
            cKw["asset_name"] = new PyString(c.AssetName);
            cKw["asset_format"] = new PyString(c.AssetFormat);
            cKw["asset_path"] = new PyString(c.AssetPath);
            cKw["description"] = new PyString(c.Description);
            cKw["tags"] = new PyList(c.Tags.Select(t => new PyString(t)).ToArray());
            cKw["generated_at"] = Runtime.None;
            pyCandidates.Append(schemas.SearchQueryCandidate.Invoke(Array.Empty<PyObject>(), cKw));
        }

        var kw = new PyDict();
        kw["provider"] = new PyString(request.Provider);
        kw["model"] = new PyString(request.Model);
        kw["query"] = new PyString(request.Query);
        kw["candidates"] = pyCandidates;
        kw["final_top_k"] = new PyInt(request.FinalTopK);
        return schemas.SearchQueryRequest.Invoke(Array.Empty<PyObject>(), kw);
    }

    private static BackendSearchIndexResponse ConvertIndexResponse(dynamic pyResponse)
    {
        var vector = (double[])pyResponse.vector;
        var vectorJson = JsonSerializer.Serialize(vector.Select(v => (float)v).ToArray());

        return new BackendSearchIndexResponse(
            AssetId: (string)pyResponse.asset_id,
            AssetName: (string)pyResponse.asset_name,
            AssetFormat: (string)pyResponse.asset_format,
            AssetPath: (string)pyResponse.asset_path,
            Description: (string)pyResponse.description,
            Vector: JsonSerializer.Deserialize<JsonElement>(vectorJson),
            VectorDim: (int)pyResponse.vector_dim,
            EmbeddingModel: (string)pyResponse.embedding_model,
            TokenUsage: ConvertTokenUsage(pyResponse.token_usage));
    }

    private static BackendSearchQueryResponse ConvertQueryResponse(dynamic pyResponse)
    {
        var results = new System.Collections.Generic.List<BackendSearchQueryResult>();
        var pyResults = (PyObject)pyResponse.results;
        int count = (int)pyResults.Length();
        for (int i = 0; i < count; i++)
        {
            using var item = pyResults[i];
            dynamic dynamicItem = item;
            results.Add(new BackendSearchQueryResult(
                CandidateId: dynamicItem.candidate_id?.As<string>(),
                RerankScore: (float)(double)dynamicItem.rerank_score.As<double>()));
        }

        return new BackendSearchQueryResponse(
            Query: (string)pyResponse.query,
            FinalTopK: (int)pyResponse.final_top_k,
            RerankModel: (string)pyResponse.rerank_model,
            Results: results.ToArray(),
            TokenUsage: ConvertTokenUsage(pyResponse.token_usage));
    }

    private static int? ConvertTokenUsage(dynamic tokenUsage)
    {
        if (tokenUsage == null)
            return null;
        return (int)tokenUsage;
    }
}