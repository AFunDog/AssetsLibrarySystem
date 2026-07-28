using System;
using System.Threading.Tasks;

namespace AssetsLibrarySystem.Avalonia.Services.Backend;

/// <summary>后端会话服务，纯操作，不持有 UI 状态</summary>
public interface IBackendSessionService
{
    bool IsBackendReady { get; }
    string BaseUrl { get; }

    Task InitializeAsync();
    event Action? BackendStatusChanged;
}