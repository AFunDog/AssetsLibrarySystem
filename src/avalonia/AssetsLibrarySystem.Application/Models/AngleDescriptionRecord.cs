namespace AssetsLibrarySystem.Application.Models;

public sealed record AngleDescriptionRecord(
    string AngleKey,
    string Label,
    string Text,
    string[] Tags,
    int MaxLength)
{
    public string TagsDisplay => string.Join("、", Tags);
}