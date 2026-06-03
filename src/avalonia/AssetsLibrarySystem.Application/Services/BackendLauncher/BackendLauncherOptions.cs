using System;

namespace AssetsLibrarySystem.Application.Services.BackendLauncher;

public sealed class BackendLauncherOptions
{
    public string DebugPythonPath { get; init; } = @".venv\Scripts\python.exe";

    public string DebugArgumentsTemplate { get; init; } = "-m uvicorn app.main:app --host {host} --port {port}";

    public string PublishedExecutablePath { get; init; } = "assets-library-system-backend.exe";

    public string PublishedArgumentsTemplate { get; init; } = "--host {host} --port {port}";

    public required string BackendWorkingDirectory { get; init; }

    public required string DataRoot { get; init; }

    public string Host { get; init; } = "127.0.0.1";

    public int Port { get; init; } = 8000;

    public TimeSpan StartupTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan HealthCheckInterval { get; init; } = TimeSpan.FromMilliseconds(500);

    public TimeSpan HeartbeatInterval { get; init; } = TimeSpan.FromSeconds(2);

    public TimeSpan HeartbeatTimeout { get; init; } = TimeSpan.FromSeconds(8);

    public TimeSpan HeartbeatStartupGrace { get; init; } = TimeSpan.FromSeconds(15);
}
