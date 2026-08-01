using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AssetsLibrarySystem.Application.Models;
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

    public AssetDetailViewModel(LibraryWorkspaceViewModel workspace)
    {
        Workspace = workspace;
        Workspace.PropertyChanged += (_, e) =>
        {
            OnPropertyChanged(e.PropertyName);
            if (e.PropertyName == nameof(Workspace.SelectedAssetType) ||
                e.PropertyName == nameof(Workspace.SelectedAssetName) ||
                e.PropertyName == nameof(Workspace.SelectedAssetPath))
            {
                _ = LoadPreviewAsync();
            }

            if (e.PropertyName is nameof(Workspace.SelectedAsset) or nameof(Workspace.SelectedAssetType))
            {
                OnPropertyChanged(nameof(CanChangeAssetType));
                OnPropertyChanged(nameof(ChangeAssetTypeTargetLabel));
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
            || text.Contains("场景分割完成", StringComparison.Ordinal))
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

    private async Task LoadPreviewAsync()
    {
        var generation = ++PreviewGeneration;
        var assetType = Workspace.SelectedAssetType;
        var assetPath = Workspace.SelectedAssetPath;

        var previous = PreviewImage;
        HasPreview = false;
        IsImagePreview = false;
        IsTextPreview = false;
        IsMediaPlaceholder = false;
        PreviewImage = null;
        previous?.Dispose();
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
            case "音频":
                if (generation != PreviewGeneration)
                    return;
                HasPreview = true;
                IsMediaPlaceholder = true;
                break;
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