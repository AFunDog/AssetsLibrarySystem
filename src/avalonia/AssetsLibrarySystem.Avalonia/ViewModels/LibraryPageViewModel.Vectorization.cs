using System.Threading.Tasks;
using AssetsLibrarySystem.Avalonia.Models;

namespace AssetsLibrarySystem.Avalonia.ViewModels;

public sealed partial class LibraryPageViewModel
{
    public Task VectorizeDescriptionsForNodeAsync(AssetLibraryTreeNode? node) =>
        VectorizationPanel.VectorizeDescriptionsForNodeAsync(node);
}
