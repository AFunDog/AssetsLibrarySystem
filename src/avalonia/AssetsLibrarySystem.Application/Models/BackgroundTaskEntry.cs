using CommunityToolkit.Mvvm.ComponentModel;

namespace AssetsLibrarySystem.Avalonia.Models;

public sealed partial class BackgroundTaskEntry : ObservableObject
{
    public required string Id { get; init; }

    public long Sequence { get; set; }

    [ObservableProperty]
    public partial string Title { get; set; }

    [ObservableProperty]
    public partial string StageText { get; set; }

    [ObservableProperty]
    public partial string DetailText { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; }

    [ObservableProperty]
    public partial string StartedAtText { get; set; }

    [ObservableProperty]
    public partial string TimelineText { get; set; }

    [ObservableProperty]
    public partial bool IsActive { get; set; }
}
