using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AssetsLibrarySystem.Application.Models;
using AssetsLibrarySystem.Application.Services.Python;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AssetsLibrarySystem.Avalonia.ViewModels;

/// <summary>
/// 素材详情 ViewModel，委托给 LibraryWorkspaceViewModel。
/// AXAML 通过此 ViewModel 访问素材详情状态。
/// </summary>
public sealed partial class AssetDetailViewModel : ObservableObject
{
    private LibraryWorkspaceViewModel Workspace { get; }
    private VideoFrameService? VideoFrameService { get; }

    public AssetDetailViewModel(LibraryWorkspaceViewModel workspace, VideoFrameService? videoFrameService = null)
    {
        Workspace = workspace;
        VideoFrameService = videoFrameService;
        Workspace.PropertyChanged += (_, e) =>
        {
            OnPropertyChanged(e.PropertyName);
            // 预览只依赖素材对象与类型：移除 Name/Path 触发，避免素材切换时
            // 同一次选中触发 3 次并发预览加载（含 ffmpeg 首帧提取）
            if (e.PropertyName == nameof(Workspace.SelectedAsset) ||
                e.PropertyName == nameof(Workspace.SelectedAssetType))
            {
                _ = LoadPreviewAsync();
            }

            if (e.PropertyName is nameof(Workspace.SelectedAsset) or nameof(Workspace.SelectedAssetType))
            {
                OnPropertyChanged(nameof(CanChangeAssetType));
                OnPropertyChanged(nameof(ChangeAssetTypeTargetLabel));
            }

            // SelectedAsset 引用可能不变（类型/名称等原地修改），HasSelectedAsset 依赖引用非空，
            // 这里按选中状态显式通知，避免详情头部可见性卡在初始值
            if (e.PropertyName is nameof(Workspace.SelectedAsset) or nameof(Workspace.SelectedAssetType))
            {
                OnPropertyChanged(nameof(HasSelectedAsset));
                OnPropertyChanged(nameof(IsAssetSelected));
            }

            if (e.PropertyName is nameof(Workspace.SelectedLibrary))
            {
                OnPropertyChanged(nameof(IsLibrarySelected));
            }

            if (e.PropertyName is nameof(Workspace.SelectedAsset) or nameof(Workspace.SelectedAssetDescriptionAngles)
                or nameof(Workspace.SelectedAssetSegmentDescriptionGroups)
                or nameof(Workspace.SelectedAssetType))
            {
                OnPropertyChanged(nameof(ShowAngleEmptyHint));
                // 类型修改（视频↔视频剪辑）是原地改 AssetType，SelectedAsset 引用不变，
                // 需要随 SelectedAssetType 变化刷新分组/平铺展示状态
                OnPropertyChanged(nameof(IsClipAssetSelected));
            }

            if (e.PropertyName is nameof(Workspace.SelectedAsset) or nameof(Workspace.SelectedAssetType))
            {
                OnPropertyChanged(nameof(IsImageAssetSelected));
                OnPropertyChanged(nameof(IsTextAssetSelected));
                OnPropertyChanged(nameof(IsMediaAssetSelected));
            }

            if (e.PropertyName == nameof(Workspace.SelectedAssetDescriptionText) ||
                e.PropertyName == nameof(Workspace.SelectedAsset))
            {
                SyncEditDescriptionFromWorkspace();
                OnPropertyChanged(nameof(SelectedAssetTags));
            }
        };

        SyncEditDescriptionFromWorkspace();
    }

    private void SyncEditDescriptionFromWorkspace()
    {
        // 占位文案不写入编辑框，避免用户误保存
        var text = Workspace.SelectedAssetDescriptionText;
        if (string.IsNullOrWhiteSpace(text)
            || text.Contains("还没有可显示", StringComparison.Ordinal)
            || text.Contains("点击“描述", StringComparison.Ordinal)
            || text.Contains("描述记录已删除", StringComparison.Ordinal)
            || text.Contains("描述存储未就绪", StringComparison.Ordinal)
            || text.Contains("描述记录读取失败", StringComparison.Ordinal)
            || text.Contains("场景分割完成", StringComparison.Ordinal)
            || text.Contains("未配置模型 API Key", StringComparison.Ordinal))
        {
            EditDescriptionText = string.Empty;
            return;
        }

        EditDescriptionText = text;
    }

    [Obsolete("仅供设计器使用")]
    public AssetDetailViewModel()
        : this(new LibraryWorkspaceViewModel())
    {
    }

    // ===== 委托给 Workspace =====
    public string SelectedAssetName => Workspace.SelectedAssetName;
    public string SelectedAssetLibrary => Workspace.SelectedAssetLibrary;
    public string SelectedAssetPath => Workspace.SelectedAssetPath;
    public string SelectedAssetType => Workspace.SelectedAssetType;
    public string SelectedAssetSubtype => Workspace.SelectedAssetSubtype;
    public string SelectedAssetStage => Workspace.SelectedAssetStage;
    public string SelectedAssetAiState => Workspace.SelectedAssetAiState;
    public string SelectedAssetDetail => Workspace.SelectedAssetDetail;
    public string SelectedAssetDescriptionState => Workspace.SelectedAssetDescriptionState;
    public string SelectedAssetDescriptionGeneratedAt => Workspace.SelectedAssetDescriptionGeneratedAt;
    public string SelectedAssetDescriptionText => Workspace.SelectedAssetDescriptionText;
    public string SelectedAssetDescriptionStorePath => Workspace.SelectedAssetDescriptionStorePath;
    public string SelectedAssetDescriptionMode => Workspace.SelectedAssetDescriptionMode;
    public string SelectedAssetDescriptionTokenUsage => Workspace.SelectedAssetDescriptionTokenUsage;
    public string SelectedAssetDescriptionPrompt => Workspace.SelectedAssetDescriptionPrompt;
    public string SelectedAssetDescriptionSystemPrompt => Workspace.SelectedAssetDescriptionSystemPrompt;
    public ObservableCollection<AngleDescriptionRecord> SelectedAssetDescriptionAngles
        => Workspace.SelectedAssetDescriptionAngles;

    /// <summary>片段描述分组（剪辑素材按时间切片分组展示）</summary>
    public ObservableCollection<SegmentDescriptionGroupViewModel> SelectedAssetSegmentDescriptionGroups
        => Workspace.SelectedAssetSegmentDescriptionGroups;

    /// <summary>当前是否选中了素材（头部/操作区可见性：预览失败时也不隐藏操作）</summary>
    public bool HasSelectedAsset => Workspace.SelectedAsset is not null;

    /// <summary>当前是否选中了素材（重命名素材行可见性）</summary>
    public bool IsAssetSelected => Workspace.SelectedAsset is not null;

    /// <summary>当前是否选中了素材库（重命名库行可见性）</summary>
    public bool IsLibrarySelected => Workspace.SelectedLibrary is not null;

    /// <summary>当前选中素材是否为剪辑素材（控制分组/平铺两种描述展示）</summary>
    public bool IsClipAssetSelected => Workspace.SelectedAsset is { AssetType: "视频剪辑" };

    /// <summary>当前选中素材是否为图片（控制预览与字段布局）</summary>
    public bool IsImageAssetSelected => Workspace.SelectedAssetType == "图片";

    /// <summary>当前选中素材是否为文本</summary>
    public bool IsTextAssetSelected => Workspace.SelectedAssetType == "文本";

    /// <summary>当前选中素材是否为普通视频/音频（无片段概念）</summary>
    public bool IsMediaAssetSelected => Workspace.SelectedAssetType is "视频" or "音频";

    /// <summary>角度列表为空且选中素材未描述时，显示空态引导（替代大块空白）</summary>
    public bool ShowAngleEmptyHint
        => SelectedAssetDescriptionAngles.Count == 0
           && SelectedAssetSegmentDescriptionGroups.Count == 0
           && Workspace.SelectedAsset is { IsDescribed: false };

    // ===== 片段列表（已分割剪辑素材） =====
    public ObservableCollection<SegmentListItemViewModel> SelectedAssetSegmentItems
        => Workspace.SelectedAssetSegmentItems;

    public bool HasSelectedAssetSegments => Workspace.HasSelectedAssetSegments;

    // ===== 标签编辑 =====
    public ObservableCollection<string> SelectedAssetTags => Workspace.SelectedAsset?.Tags ?? [];

    [ObservableProperty]
    public partial string NewTagText { get; set; } = string.Empty;

    [RelayCommand]
    private async Task AddTagAsync()
    {
        if (string.IsNullOrWhiteSpace(NewTagText) || Workspace.SelectedAsset is null)
            return;
        var tag = NewTagText.Trim();
        var currentTags = Workspace.SelectedAsset.Tags.ToArray();
        if (currentTags.Contains(tag)) return;
        await Workspace.UpdateSelectedAssetTagsAsync(currentTags.Append(tag).ToArray());
        NewTagText = string.Empty;
    }

    [RelayCommand]
    private async Task RemoveTagAsync(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag) || Workspace.SelectedAsset is null) return;
        var currentTags = Workspace.SelectedAsset.Tags.ToArray();
        await Workspace.UpdateSelectedAssetTagsAsync(currentTags.Where(t => t != tag).ToArray());
    }

    // ===== 删除操作 =====
    [RelayCommand]
    private async Task DeleteAssetAsync()
    {
        if (Workspace.DeleteAssetCommand.CanExecute(null))
            await Workspace.DeleteAssetCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private async Task DeleteLibraryAsync()
    {
        if (Workspace.DeleteLibraryCommand.CanExecute(null))
            await Workspace.DeleteLibraryCommand.ExecuteAsync(null);
    }

    // ===== 重命名 =====
    [ObservableProperty]
    public partial string RenameText { get; set; } = string.Empty;

    [RelayCommand]
    private async Task RenameAssetAsync()
    {
        if (string.IsNullOrWhiteSpace(RenameText)) return;
        await Workspace.UpdateSelectedAssetNameAsync(RenameText.Trim());
        RenameText = string.Empty;
    }

    [RelayCommand]
    private async Task RenameLibraryAsync()
    {
        if (string.IsNullOrWhiteSpace(RenameText)) return;
        await Workspace.UpdateSelectedLibraryNameAsync(RenameText.Trim());
        RenameText = string.Empty;
    }

    // ===== 类型修改（视频 ↔ 视频剪辑） =====
    /// <summary>当前选中素材是否为视频类（显示「更改类型」入口）</summary>
    public bool CanChangeAssetType =>
        Workspace.SelectedAsset is { AssetType: "视频" or "视频剪辑" };

    /// <summary>转换目标类型文案，如「切换为视频剪辑」</summary>
    public string ChangeAssetTypeTargetLabel =>
        Workspace.SelectedAssetType == "视频剪辑" ? "切换为视频" : "切换为视频剪辑";

    [RelayCommand]
    private async Task ChangeAssetTypeAsync()
    {
        var asset = Workspace.SelectedAsset;
        if (asset is null || !CanChangeAssetType) return;
        var targetType = asset.AssetType == "视频剪辑" ? "视频" : "视频剪辑";
        await Workspace.UpdateSelectedAssetTypeAsync(targetType);
        OnPropertyChanged(nameof(CanChangeAssetType));
        OnPropertyChanged(nameof(ChangeAssetTypeTargetLabel));
    }

    // ===== 描述编辑 =====
    [ObservableProperty]
    public partial string EditDescriptionText { get; set; } = string.Empty;

    [RelayCommand]
    private async Task SaveDescriptionAsync()
    {
        if (string.IsNullOrWhiteSpace(EditDescriptionText)) return;
        await Workspace.UpdateSelectedAssetDescriptionAsync(EditDescriptionText.Trim());
    }

    // ===== 预览 =====
    private int PreviewGeneration { get; set; }

    [ObservableProperty]
    public partial Bitmap? PreviewImage { get; set; }

    [ObservableProperty]
    public partial bool HasPreview { get; set; }

    [ObservableProperty]
    public partial bool IsImagePreview { get; set; }

    [ObservableProperty]
    public partial bool IsTextPreview { get; set; }

    [ObservableProperty]
    public partial bool IsMediaPlaceholder { get; set; }

    [ObservableProperty]
    public partial string PreviewText { get; set; } = string.Empty;

    // ===== 视频首帧封面（视频/视频剪辑素材） =====
    [ObservableProperty]
    public partial Bitmap? PreviewCoverImage { get; set; }

    [ObservableProperty]
    public partial bool HasPreviewCover { get; set; }

    private async Task LoadPreviewAsync()
    {
        var generation = ++PreviewGeneration;
        var assetType = Workspace.SelectedAssetType;
        var assetPath = Workspace.SelectedAssetPath;

        var previous = PreviewImage;
        var previousCover = PreviewCoverImage;
        HasPreview = false;
        IsImagePreview = false;
        IsTextPreview = false;
        IsMediaPlaceholder = false;
        HasPreviewCover = false;
        PreviewImage = null;
        PreviewCoverImage = null;
        previous?.Dispose();
        previousCover?.Dispose();
        PreviewText = string.Empty;

        if (string.IsNullOrWhiteSpace(assetPath) || !File.Exists(assetPath))
            return;

        switch (assetType)
        {
            case "图片":
                await LoadImagePreviewAsync(assetPath, generation);
                break;
            case "文本":
                await LoadTextPreviewAsync(assetPath, generation);
                break;
            case "视频":
            case "视频剪辑":
                // 视频/剪辑素材：优先提取首帧封面，失败回退类型占位
                HasPreview = true;
                await LoadVideoCoverAsync(assetPath, generation);
                if (generation == PreviewGeneration && !HasPreviewCover)
                {
                    IsMediaPlaceholder = true;
                }

                break;
            case "音频":
                HasPreview = true;
                IsMediaPlaceholder = true;
                break;
        }
    }

    private async Task LoadVideoCoverAsync(string path, int generation)
    {
        if (VideoFrameService is null)
        {
            return;
        }

        try
        {
            var jpegBytes = await Task.Run(() => VideoFrameService.ExtractFrame(path, 0.0)).ConfigureAwait(true);
            if (generation != PreviewGeneration || jpegBytes is null || jpegBytes.Length == 0)
            {
                return;
            }

            var previous = PreviewCoverImage;
            PreviewCoverImage = new Bitmap(new MemoryStream(jpegBytes));
            previous?.Dispose();
            HasPreviewCover = true;
        }
        catch
        {
            // 首帧提取失败保持占位，静默回退
        }
    }

    private async Task LoadImagePreviewAsync(string path, int generation)
    {
        try
        {
            var bitmap = await Task.Run(() =>
            {
                var bmp = new Bitmap(path);
                // 缩放至最大 300px
                var maxSide = 300;
                if (bmp.PixelSize.Width <= maxSide && bmp.PixelSize.Height <= maxSide)
                    return bmp;
                var scale = Math.Min(
                    (double)maxSide / bmp.PixelSize.Width,
                    (double)maxSide / bmp.PixelSize.Height);
                var w = (int)(bmp.PixelSize.Width * scale);
                var h = (int)(bmp.PixelSize.Height * scale);
                var resized = bmp.CreateScaledBitmap(
                    new global::Avalonia.PixelSize(w, h),
                    BitmapInterpolationMode.HighQuality);
                bmp.Dispose();
                return resized;
            });

            if (generation != PreviewGeneration)
            {
                bitmap.Dispose();
                return;
            }

            var previous = PreviewImage;
            PreviewImage = bitmap;
            previous?.Dispose();
            HasPreview = true;
            IsImagePreview = true;
        }
        catch
        {
            // 预览失败时静默回退
        }
    }

    private async Task LoadTextPreviewAsync(string path, int generation)
    {
        try
        {
            var text = await Task.Run(() =>
            {
                foreach (var encoding in new[] { "utf-8-sig", "utf-8", "gb18030" })
                {
                    try
                    {
                        var content = File.ReadAllText(path, System.Text.Encoding.GetEncoding(encoding));
                        return content.Length > 500 ? content[..500] + "..." : content;
                    }
                    catch { }
                }
                return null;
            });

            if (generation != PreviewGeneration)
                return;

            if (text is not null)
            {
                PreviewText = text;
                HasPreview = true;
                IsTextPreview = true;
            }
        }
        catch
        {
            // 预览失败时静默回退
        }
    }
}