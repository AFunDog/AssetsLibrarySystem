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
        CancellationToken ct = default,
        Action<int>? progress = null)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            return Engine.Execute<BackendModelGenerateResponse>(() =>
            {
                Log.Information(
                    "PythonModelService 调用 generate_text: format={Format}, path={Path}, mock={Mock}",
                    request.AssetFormat, request.AssetPath, request.MockResponse);

                try
                {
                    dynamic modelService = GetModelService();
                    var pyRequest = BuildGenerateRequest(request);
                    // generate_text 是 Python async 函数：必须用 asyncio.run 执行，
                    // 否则拿到的是未执行的 coroutine，访问属性会抛 AttributeError。
                    dynamic asyncio = Py.Import("asyncio");
                    dynamic pyResponse;
                    if (progress is not null)
                    {
                        // 把 .NET 回调包装成 Python 可调用对象：返回 False 表示请求取消
                        dynamic progressCallback = PyObject.FromManagedObject(
                            new Func<int, bool>(percent =>
                            {
                                if (ct.IsCancellationRequested)
                                {
                                    return false;
                                }

                                progress(percent);
                                return true;
                            }));
                        pyResponse = asyncio.run(modelService.generate_text(pyRequest, progress_callback: progressCallback));
                    }
                    else
                    {
                        pyResponse = asyncio.run(modelService.generate_text(pyRequest));
                    }

                    return ConvertResponse(pyResponse);
                }
                catch (PythonException ex)
                {
                    // Python 侧场景检测取消（进度回调返回 False 触发）
                    if (ex.Type?.Name.Contains("SceneDetectionCancelled", StringComparison.Ordinal) == true)
                    {
                        throw new OperationCanceledException("分割任务已取消", ex);
                    }

                    Log.Error("Python generate_text 调用失败: {Traceback}", ex.StackTrace);
                    throw new InvalidOperationException(
                        $"Python 描述服务调用失败：{ex.Message}", ex);
                }
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
        kw["subtype"] = request.Subtype is not null ? new PyString(request.Subtype) : Runtime.None;

        if (request.Angles is { Length: > 0 })
        {
            var pyAngles = new PyList();
            foreach (var angle in request.Angles)
            {
                var angleKw = new PyDict();
                angleKw["key"] = new PyString(angle.Key);
                angleKw["label"] = new PyString(angle.Label);
                angleKw["prompt"] = new PyString(angle.Prompt);
                angleKw["max_length"] = new PyInt(angle.MaxLength);
                pyAngles.Append(schemas.AngleDef.Invoke(Array.Empty<PyObject>(), angleKw));
            }
            kw["angles"] = pyAngles;
        }
        else
        {
            kw["angles"] = Runtime.None;
        }

        kw["enable_slicing"] = new PyInt(request.EnableSlicing ? 1 : 0);
        kw["slice_threshold"] = new PyFloat(request.SliceThreshold);
        kw["min_scene_len"] = new PyInt(request.MinSceneLen);
        kw["adaptive_threshold"] = new PyFloat(request.AdaptiveThreshold);
        kw["slicing_only"] = new PyInt(request.SlicingOnly ? 1 : 0);
        kw["range_start"] = request.RangeStart is not null ? new PyFloat(request.RangeStart.Value) : Runtime.None;
        kw["range_end"] = request.RangeEnd is not null ? new PyFloat(request.RangeEnd.Value) : Runtime.None;

        if (request.ExistingSegments is { Length: > 0 })
        {
            var pySegments = new PyList();
            foreach (var segment in request.ExistingSegments)
            {
                var segKw = new PyDict();
                segKw["start"] = new PyFloat(segment.Start);
                segKw["end"] = new PyFloat(segment.End);
                pySegments.Append(schemas.SegmentRange.Invoke(Array.Empty<PyObject>(), segKw));
            }
            kw["existing_segments"] = pySegments;
        }
        else
        {
            kw["existing_segments"] = Runtime.None;
        }

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