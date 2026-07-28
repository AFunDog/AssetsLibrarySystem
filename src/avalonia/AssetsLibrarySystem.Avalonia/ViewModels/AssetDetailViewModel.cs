using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using AssetsLibrarySystem.Application.Models;
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
        Workspace.PropertyChanged += (_, e) => OnPropertyChanged(e.PropertyName);
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

    // ===== 描述编辑 =====
    [ObservableProperty]
    public partial string EditDescriptionText { get; set; } = string.Empty;

    [RelayCommand]
    private async Task SaveDescriptionAsync()
    {
        if (string.IsNullOrWhiteSpace(EditDescriptionText)) return;
        await Workspace.UpdateSelectedAssetDescriptionAsync(EditDescriptionText.Trim());
    }
}