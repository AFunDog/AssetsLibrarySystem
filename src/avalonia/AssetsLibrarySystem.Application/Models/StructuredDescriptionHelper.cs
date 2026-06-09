using System;
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
}
