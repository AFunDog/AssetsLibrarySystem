using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AssetsLibrarySystem.Application.Models;

namespace AssetsLibrarySystem.Application.Services.AssetLibrary;

public interface IAssetLibraryService
{
    Task<IReadOnlyList<LibraryWorkspace>> GetLibrariesAsync(CancellationToken ct = default);

    Task<LibraryWorkspace> AddLibraryAsync(string folderPath, CancellationToken ct = default);

    /// <summary>按指定类型登记素材库（clip=视频剪辑库，只收视频并按片段描述）</summary>
    Task<LibraryWorkspace> AddLibraryAsync(string folderPath, LibraryKind kind, CancellationToken ct = default);

    Task<IReadOnlyList<ManagedAssetRecord>> ScanLibraryAsync(LibraryWorkspace library, CancellationToken ct = default);

    // === 新增：CRUD 操作 ===

    /// <summary>删除素材库及其所有关联数据（素材、描述、向量）</summary>
    Task DeleteLibraryAsync(long libraryId, CancellationToken ct = default);

    /// <summary>更新素材库名称</summary>
    Task UpdateLibraryAsync(long libraryId, string newName, CancellationToken ct = default);

    /// <summary>删除单个素材及其关联数据（描述、向量）</summary>
    Task DeleteAssetAsync(long assetId, CancellationToken ct = default);

    /// <summary>更新素材标签（持久化到 asset_metadata.tags_json）</summary>
    Task UpdateAssetTagsAsync(long assetId, string[] tags, CancellationToken ct = default);

    /// <summary>更新素材名称（同步更新 assets 和 asset_descriptions）</summary>
    Task UpdateAssetNameAsync(long assetId, string newName, CancellationToken ct = default);
}
