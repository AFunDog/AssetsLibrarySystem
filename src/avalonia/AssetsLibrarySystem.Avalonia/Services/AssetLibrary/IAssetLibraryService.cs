using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AssetsLibrarySystem.Avalonia.Models;

namespace AssetsLibrarySystem.Avalonia.Services.AssetLibrary;

public interface IAssetLibraryService
{
    Task<IReadOnlyList<LibraryWorkspace>> GetLibrariesAsync(CancellationToken ct = default);

    Task<LibraryWorkspace> AddLibraryAsync(string folderPath, CancellationToken ct = default);

    Task<IReadOnlyList<ManagedAssetRecord>> ScanLibraryAsync(LibraryWorkspace library, CancellationToken ct = default);
}
