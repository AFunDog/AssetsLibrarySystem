namespace AssetsLibrarySystem.Application.Models;

public sealed class BackgroundTaskEntry : ObservableModel
{
    public required string Id { get; init; }

    public long Sequence { get; set; }

    public string Title
    {
        get => field;
        set => SetProperty(ref field, value);
    } = string.Empty;

    public string StageText
    {
        get => field;
        set => SetProperty(ref field, value);
    } = string.Empty;

    public string DetailText
    {
        get => field;
        set => SetProperty(ref field, value);
    } = string.Empty;

    public string StatusText
    {
        get => field;
        set => SetProperty(ref field, value);
    } = string.Empty;

    public string StartedAtText
    {
        get => field;
        set => SetProperty(ref field, value);
    } = string.Empty;

    public string TimelineText
    {
        get => field;
        set => SetProperty(ref field, value);
    } = string.Empty;

    public bool IsActive
    {
        get => field;
        set => SetProperty(ref field, value);
    }
}
