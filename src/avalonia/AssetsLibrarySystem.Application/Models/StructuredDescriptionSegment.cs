using System;

namespace AssetsLibrarySystem.Application.Models;

public sealed record StructuredDescriptionSegment(
    string AngleType,
    string Text)
{
    public string NormalizedAngleType => string.IsNullOrWhiteSpace(AngleType)
        ? AssetDescriptionVectorDocument.DefaultAngleType
        : AngleType.Trim();

    public string NormalizedText => Text.Trim();
}
