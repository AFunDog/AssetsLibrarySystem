using CommunityToolkit.Mvvm.ComponentModel;
using AssetsLibrarySystem.Avalonia.Services.BackendLauncher;

namespace AssetsLibrarySystem.Avalonia.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly IBackendLauncher _backendLauncher;

    public MainWindowViewModel() : this(null!)
    {
        
    }

    public MainWindowViewModel(IBackendLauncher backendLauncher)
    {
        _backendLauncher = backendLauncher;
    }
    
}
