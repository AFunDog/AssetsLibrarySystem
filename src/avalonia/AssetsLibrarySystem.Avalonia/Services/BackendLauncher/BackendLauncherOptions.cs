using System;

namespace AssetsLibrarySystem.Avalonia.Services.BackendLauncher;

/// <summary>
/// 后端启动参数。
/// </summary>
public sealed class BackendLauncherOptions
{
    /// <summary>
    /// Python 解释器路径，默认 "python"（依赖 PATH 环境变量）。
    /// </summary>
    public string PythonPath { get; init; } = "python";

    /// <summary>
    /// 后端项目的工作目录（即 src/backend 所在路径）。
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
