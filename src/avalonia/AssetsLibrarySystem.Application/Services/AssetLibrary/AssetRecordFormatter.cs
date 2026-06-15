using System;
using System.Collections.Generic;
using System.Linq;

namespace AssetsLibrarySystem.Application.Services.AssetLibrary;

internal static class AssetRecordFormatter
{
    public static string[] BuildTags(string assetType, string extension, IEnumerable<string> metadataTags)
    {
        return metadataTags
            .Append(assetType)
            .Append(extension)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static string BuildSummary(string assetType, long bytes, DateTime modifiedTime, string assetUid, string status)
    {
        var statusText = status switch
        {
            "changed" => "内容变化",
            "ok" => "已同步",
            _ => status
        };

        return $"{assetType}文件 · {FormatFileSize(bytes)} · 修改于 {modifiedTime:yyyy-MM-dd HH:mm} · {statusText} · UID {assetUid}";
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        if (bytes < 1024 * 1024)
        {
            return $"{bytes / 1024d:F1} KB";
        }

        if (bytes < 1024L * 1024 * 1024)
        {
            return $"{bytes / 1024d / 1024:F1} MB";
        }

        return $"{bytes / 1024d / 1024 / 1024:F1} GB";
    }
}
