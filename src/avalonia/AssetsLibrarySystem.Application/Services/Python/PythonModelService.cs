using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AssetsLibrarySystem.Application.Services.BackendApi;
using Python.Runtime;
using Serilog;

namespace AssetsLibrarySystem.Application.Services.Python;

public sealed class PythonModelService : IBackendModelClient
{
    private PythonEngineService Engine { get; }

    public PythonModelService(PythonEngineService engine)
    {
        Engine = engine;
    }

    public Task<BackendModelGenerateResponse> GenerateAsync(
        string backendBaseUrl,
        BackendModelGenerateRequest request,
        CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            return Engine.Execute<BackendModelGenerateResponse>(() =>
            {
                Log.Information(
                    "PythonModelService 调用 generate_text: format={Format}, path={Path}, mock={Mock}",
                    request.AssetFormat, request.AssetPath, request.MockResponse);

                dynamic modelService = GetModelService();
                var pyRequest = BuildGenerateRequest(request);
                dynamic pyResponse = modelService.generate_text(pyRequest);
                return ConvertResponse(pyResponse);
            });
        }, ct);
    }

    private static dynamic GetModelService()
    {
        dynamic app = Py.Import("app.application.services.model_service");
        return app.ModelService();
    }

    private static PyObject BuildGenerateRequest(BackendModelGenerateRequest request)
    {
        dynamic schemas = Py.Import("app.schemas.model");
        var kw = new PyDict();
        kw["asset_format"] = new PyString(request.AssetFormat);
        kw["asset_path"] = new PyString(request.AssetPath);
        kw["prompt"] = request.Prompt is not null ? new PyString(request.Prompt) : Runtime.None;
        kw["system_prompt"] = request.SystemPrompt is not null ? new PyString(request.SystemPrompt) : Runtime.None;
        kw["mock_response"] = new PyInt(request.MockResponse ? 1 : 0);
        return schemas.ModelGenerateRequest.Invoke(Array.Empty<PyObject>(), kw);
    }

    private static BackendModelGenerateResponse ConvertResponse(dynamic pyResponse)
    {
        BackendTokenUsage? tokenUsage = null;
        if (pyResponse.token_usage != null)
        {
            var tu = pyResponse.token_usage;
            tokenUsage = new BackendTokenUsage(
                InputTokens: (int)tu.input_tokens,
                OutputTokens: (int)tu.output_tokens,
                TotalTokens: (int)tu.total_tokens,
                ImageTokens: SafeInt(tu.image_tokens),
                VideoTokens: SafeInt(tu.video_tokens),
                AudioTokens: SafeInt(tu.audio_tokens),
                InputTokensDetails: SafeJson(tu.input_tokens_details),
                OutputTokensDetails: SafeJson(tu.output_tokens_details),
                PromptTokensDetails: SafeJson(tu.prompt_tokens_details));
        }

        return new BackendModelGenerateResponse(
            ProviderSlot: (string)pyResponse.provider_slot,
            Provider: (string)pyResponse.provider,
            Model: (string)pyResponse.model,
            Mode: (string)pyResponse.mode,
            OutputText: (string)pyResponse.output_text,
            SystemPrompt: (string)pyResponse.system_prompt,
            TokenUsage: tokenUsage);
    }

    private static int? SafeInt(dynamic value)
    {
        if (value == null)
            return null;
        return (int)value;
    }

    private static JsonElement? SafeJson(dynamic value)
    {
        if (value == null)
            return null;
        return JsonSerializer.SerializeToElement(value.AsManagedObject(typeof(object)));
    }
}