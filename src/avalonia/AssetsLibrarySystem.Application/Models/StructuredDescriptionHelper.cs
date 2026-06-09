using System;
using System.Collections.Generic;
using System.Text.Json;

namespace AssetsLibrarySystem.Application.Models;

public static class StructuredDescriptionHelper
{
    public static string ExtractPrimaryText(string? rawDescription)
    {
        if (string.IsNullOrWhiteSpace(rawDescription))
        {
            return string.Empty;
        }

        var trimmed = rawDescription.Trim();
        if (!trimmed.StartsWith("{", StringComparison.Ordinal))
        {
            return trimmed;
        }

        try
        {
            using var document = JsonDocument.Parse(trimmed);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return trimmed;
            }

            if (!document.RootElement.TryGetProperty("全面", out var comprehensiveElement))
            {
                return trimmed;
            }

            if (comprehensiveElement.ValueKind == JsonValueKind.String)
            {
                return comprehensiveElement.GetString()?.Trim() ?? string.Empty;
            }

            if (comprehensiveElement.ValueKind == JsonValueKind.Object
                && comprehensiveElement.TryGetProperty("text", out var textElement)
                && textElement.ValueKind == JsonValueKind.String)
            {
                return textElement.GetString()?.Trim() ?? string.Empty;
            }
        }
        catch (JsonException)
        {
            return trimmed;
        }

        return trimmed;
    }

    public static IReadOnlyList<StructuredDescriptionSegment> ExtractSegments(string? rawDescription)
    {
        if (string.IsNullOrWhiteSpace(rawDescription))
        {
            return [];
        }

        var trimmed = rawDescription.Trim();
        if (!trimmed.StartsWith("{", StringComparison.Ordinal))
        {
            return [new StructuredDescriptionSegment(AssetDescriptionVectorDocument.DefaultAngleType, trimmed)];
        }

        try
        {
            using var document = JsonDocument.Parse(trimmed);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return [new StructuredDescriptionSegment(AssetDescriptionVectorDocument.DefaultAngleType, trimmed)];
            }

            var segments = new List<StructuredDescriptionSegment>();
            foreach (var property in document.RootElement.EnumerateObject())
            {
                var text = ExtractSegmentText(property.Value);
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                segments.Add(new StructuredDescriptionSegment(property.Name, text));
            }

            if (segments.Count > 0)
            {
                SortSegments(segments);
                return segments;
            }
        }
        catch (JsonException)
        {
            return [new StructuredDescriptionSegment(AssetDescriptionVectorDocument.DefaultAngleType, trimmed)];
        }

        return [new StructuredDescriptionSegment(AssetDescriptionVectorDocument.DefaultAngleType, trimmed)];
    }

    public static string ExtractTextByAngle(string? rawDescription, string? angleType)
    {
        var normalizedAngleType = string.IsNullOrWhiteSpace(angleType)
            ? AssetDescriptionVectorDocument.DefaultAngleType
            : angleType.Trim();

        var segments = ExtractSegments(rawDescription);
        foreach (var segment in segments)
        {
            if (string.Equals(segment.NormalizedAngleType, normalizedAngleType, StringComparison.Ordinal))
            {
                return segment.NormalizedText;
            }
        }

        return ExtractPrimaryText(rawDescription);
    }

    private static string? ExtractSegmentText(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            return element.GetString()?.Trim();
        }

        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty("text", out var textElement)
            && textElement.ValueKind == JsonValueKind.String)
        {
            return textElement.GetString()?.Trim();
        }

        return null;
    }

    private static void SortSegments(List<StructuredDescriptionSegment> segments)
    {
        static int GetPriority(string angleType)
        {
            return angleType switch
            {
                "全面" => 0,
                "乐器" => 1,
                "风格" => 2,
                "情感" => 3,
                _ => 10,
            };
        }

        segments.Sort((left, right) =>
        {
            var priorityCompare = GetPriority(left.AngleType).CompareTo(GetPriority(right.AngleType));
            return priorityCompare != 0
                ? priorityCompare
                : string.Compare(left.AngleType, right.AngleType, StringComparison.Ordinal);
        });
    }
}
