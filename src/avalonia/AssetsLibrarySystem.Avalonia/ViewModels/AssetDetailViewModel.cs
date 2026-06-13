using System.ComponentModel;
using AssetsLibrarySystem.Avalonia.Services.Library;
using CommunityToolkit.Mvvm.ComponentModel;

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

    public string SelectedAssetName => LibraryCatalogService.SelectedAssetName;
    public string SelectedAssetLibrary => LibraryCatalogService.SelectedAssetLibrary;
    public string SelectedAssetPath => LibraryCatalogService.SelectedAssetPath;
    public string SelectedAssetType => LibraryCatalogService.SelectedAssetType;
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

    private void OnCatalogPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(e.PropertyName);
    }
}
