using System;

namespace AssetsLibrarySystem.Avalonia.Services.BackendLauncher;

/// <summary>
/// 后端启动参数。
/// </summary>
public sealed class BackendLauncherOptions
{
    /// <summary>
    /// 调试模式下使用的 Python 解释器路径。
    /// </summary>
    public string DebugPythonPath { get; init; } = @".venv\Scripts\python.exe";

    /// <summary>
    /// 调试模式下传给 Python 的命令参数模板。
    /// </summary>
    public string DebugArgumentsTemplate { get; init; } = "-m uvicorn app.main:app --host {host} --port {port}";

    /// <summary>
    /// 发布模式下直接启动的后端可执行文件。
    /// </summary>
    public string PublishedExecutablePath { get; init; } = "assets-library-system-backend.exe";

    /// <summary>
    /// 发布模式下传给后端可执行文件的参数模板。
    /// </summary>
    public string PublishedArgumentsTemplate { get; init; } = "--host {host} --port {port}";

    /// <summary>
    /// 后端根目录。
    /// </summary>
    public required string BackendWorkingDirectory { get; init; }

    /// <summary>
    /// 监听地址。
    /// </summary>
    public string Host { get; init; } = "127.0.0.1";

    /// <summary>
    /// 监听端口。
    /// </summary>
    public int Port { get; init; } = 8000;

    /// <summary>
    /// 启动健康检查的总超时时间。
    /// </summary>
    public TimeSpan StartupTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 每次健康检查的间隔。
    /// </summary>
    public TimeSpan HealthCheckInterval { get; init; } = TimeSpan.FromMilliseconds(500);
}
