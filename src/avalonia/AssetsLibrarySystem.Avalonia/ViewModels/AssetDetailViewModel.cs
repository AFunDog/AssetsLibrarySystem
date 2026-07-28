using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using AssetsLibrarySystem.Application.Models;
using AssetsLibrarySystem.Avalonia.Services.Library;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AssetsLibrarySystem.Avalonia.ViewModels;

public sealed partial class AssetDetailViewModel : ObservableObject
{
    private LibraryCatalogService LibraryCatalogService { get; }

    public AssetDetailViewModel()
        : this(new LibraryCatalogService())
    {
    }

    public AssetDetailViewModel(LibraryCatalogService libraryCatalogService)
    {
        LibraryCatalogService = libraryCatalogService;
        LibraryCatalogService.PropertyChanged += OnCatalogPropertyChanged;
    }

    // === 现有只读属性 ===
    public string SelectedAssetName => LibraryCatalogService.SelectedAssetName;
    public string SelectedAssetLibrary => LibraryCatalogService.SelectedAssetLibrary;
    public string SelectedAssetPath => LibraryCatalogService.SelectedAssetPath;
    public string SelectedAssetType => LibraryCatalogService.SelectedAssetType;
    public string SelectedAssetSubtype => LibraryCatalogService.SelectedAssetSubtype;
    public string SelectedAssetStage => LibraryCatalogService.SelectedAssetStage;
    public string SelectedAssetAiState => LibraryCatalogService.SelectedAssetAiState;
    public string SelectedAssetDetail => LibraryCatalogService.SelectedAssetDetail;
    public string SelectedAssetDescriptionState => LibraryCatalogService.SelectedAssetDescriptionState;
    public string SelectedAssetDescriptionGeneratedAt => LibraryCatalogService.SelectedAssetDescriptionGeneratedAt;
    public string SelectedAssetDescriptionText => LibraryCatalogService.SelectedAssetDescriptionText;
    public string SelectedAssetDescriptionStorePath => LibraryCatalogService.SelectedAssetDescriptionStorePath;
    public string SelectedAssetDescriptionMode => LibraryCatalogService.SelectedAssetDescriptionMode;
    public string SelectedAssetDescriptionTokenUsage => LibraryCatalogService.SelectedAssetDescriptionTokenUsage;
    public string SelectedAssetDescriptionPrompt => LibraryCatalogService.SelectedAssetDescriptionPrompt;
    public string SelectedAssetDescriptionSystemPrompt => LibraryCatalogService.SelectedAssetDescriptionSystemPrompt;
    public ObservableCollection<AngleDescriptionRecord> SelectedAssetDescriptionAngles
        => LibraryCatalogService.SelectedAssetDescriptionAngles;

    // === 标签编辑 ===
    public ObservableCollection<string> SelectedAssetTags => LibraryCatalogService.SelectedAsset?.Tags ?? [];

    [ObservableProperty]
    public partial string NewTagText { get; set; } = string.Empty;

    [RelayCommand]
    private async Task AddTagAsync()
    {
        if (string.IsNullOrWhiteSpace(NewTagText) || LibraryCatalogService.SelectedAsset is null)
            return;

        var tag = NewTagText.Trim();
        var currentTags = LibraryCatalogService.SelectedAsset.Tags.ToArray();
        if (currentTags.Contains(tag))
            return;

        var newTags = currentTags.Append(tag).ToArray();
        await LibraryCatalogService.UpdateSelectedAssetTagsAsync(newTags);
        NewTagText = string.Empty;
    }

    [RelayCommand]
    private async Task RemoveTagAsync(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag) || LibraryCatalogService.SelectedAsset is null)
            return;

        var currentTags = LibraryCatalogService.SelectedAsset.Tags.ToArray();
        var newTags = currentTags.Where(t => t != tag).ToArray();
        await LibraryCatalogService.UpdateSelectedAssetTagsAsync(newTags);
    }

    // === 删除操作 ===
    [RelayCommand]
    private async Task DeleteAssetAsync()
    {
        await LibraryCatalogService.DeleteSelectedAssetAsync();
    }

    [RelayCommand]
    private async Task DeleteLibraryAsync()
    {
        await LibraryCatalogService.DeleteSelectedLibraryAsync();
    }

    // === 重命名 ===
    [ObservableProperty]
    public partial string RenameText { get; set; } = string.Empty;

    [RelayCommand]
    private async Task RenameAssetAsync()
    {
        if (string.IsNullOrWhiteSpace(RenameText))
            return;
        await LibraryCatalogService.UpdateSelectedAssetNameAsync(RenameText.Trim());
        RenameText = string.Empty;
    }

    [RelayCommand]
    private async Task RenameLibraryAsync()
    {
        if (string.IsNullOrWhiteSpace(RenameText))
            return;
        await LibraryCatalogService.UpdateSelectedLibraryNameAsync(RenameText.Trim());
        RenameText = string.Empty;
    }

    // === 描述编辑 ===
    [ObservableProperty]
    public partial string EditDescriptionText { get; set; } = string.Empty;

    [RelayCommand]
    private async Task SaveDescriptionAsync()
    {
        if (string.IsNullOrWhiteSpace(EditDescriptionText))
            return;
        await LibraryCatalogService.UpdateSelectedAssetDescriptionAsync(EditDescriptionText.Trim());
    }

    private void OnCatalogPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(e.PropertyName);
        if (e.PropertyName == nameof(LibraryCatalogService.SelectedAsset)
            || e.PropertyName == nameof(LibraryCatalogService.SelectedAssetDescriptionText))
        {
            OnPropertyChanged(nameof(SelectedAssetTags));
            EditDescriptionText = LibraryCatalogService.SelectedAssetDescriptionText;
        }
    }
}