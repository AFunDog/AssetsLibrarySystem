using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using AssetsLibrarySystem.Avalonia.ViewModels;
using AssetsLibrarySystem.Avalonia.Views;
using Autofac;

namespace AssetsLibrarySystem.Avalonia;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override async void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var viewModel = ServiceBuilder.Services.Resolve<MainWindowViewModel>();
            await viewModel.InitializeAsync();

            desktop.MainWindow = new MainWindow
            {
                DataContext = viewModel,
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
    
}
