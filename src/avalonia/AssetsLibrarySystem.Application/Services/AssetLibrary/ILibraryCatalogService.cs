using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AssetsLibrarySystem.Application.Models;

namespace AssetsLibrarySystem.Application.Services.AssetLibrary;

/// <summary>素材库目录服务，纯数据操作，不持有 UI 状态</summary>
public interface ILibraryCatalogService
{
    Task<IReadOnlyList<LibraryWorkspace>> GetLibrariesAsync(CancellationToken ct = default);
    Task<LibraryWorkspace> AddLibraryAsync(string folderPath, CancellationToken ct = default);
    Task<IReadOnlyList<ManagedAssetRecord>> ScanLibraryAsync(LibraryWorkspace library, CancellationToken ct = default);

    // CRUD
    Task DeleteLibraryAsync(long libraryId, CancellationToken ct = default);
    Task UpdateLibraryAsync(long libraryId, string newName, CancellationToken ct = default);
    Task DeleteAssetAsync(long assetId, CancellationToken ct = default);
    Task UpdateAssetTagsAsync(long assetId, string[] tags, CancellationToken ct = default);
    Task UpdateAssetNameAsync(long assetId, string newName, CancellationToken ct = default);
}