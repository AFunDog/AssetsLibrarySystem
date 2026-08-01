using System;
using AssetsLibrarySystem.Application.Services.Python;
using Python.Runtime;
using Serilog;

namespace AssetsLibrarySystem.Application.Services.Python;

/// <summary>
/// 视频帧提取：通过 Python.NET 进程内调用后端 ffmpeg 提取逻辑，
/// 供剪辑素材片段列表生成缩略图。
/// </summary>
public sealed class VideoFrameService
{
    private PythonEngineService Engine { get; }

    public VideoFrameService(PythonEngineService engine)
    {
        Engine = engine;
    }

    /// <summary>提取视频指定时间点的帧，返回 JPEG 字节；失败返回 null。</summary>
    public byte[]? ExtractFrame(string videoPath, double timestamp)
    {
        return Engine.Execute<byte[]?>(() =>
        {
            try
            {
                dynamic extractor = Py.Import("app.application.services.video_frame_extractor");
                dynamic result = extractor.extract_frame(videoPath, timestamp);
                if (result is null)
                {
                    return null;
                }

                return Convert.FromBase64String((string)result);
            }
            catch (PythonException ex)
            {
                Log.Error("Python 视频帧提取失败: {Traceback}", ex.StackTrace);
                return null;
            }
            catch (Exception ex)
            {
                Log.Warning("视频帧提取异常: path={Path}, ts={Timestamp}, error={Error}",
                    videoPath, timestamp, ex.Message);
                return null;
            }
        });
    }
}
