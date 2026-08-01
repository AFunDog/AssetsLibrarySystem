using System;
using System.Collections.Generic;
using System.IO;

namespace AssetsLibrarySystem.Application.Services.AssetLibrary;

internal sealed class AssetFileScanner
{
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".yaml", ".yml", ".csv", ".log", ".xml"
    };

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".avif"
    };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".avi", ".mov", ".webm", ".wmv", ".flv"
    };

    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".wav", ".flac", ".ogg", ".m4a", ".aac", ".wma"
    };

    public IEnumerable<string> EnumerateSupportedFiles(string rootPath, bool videosOnly = false)
    {
        if (!Directory.Exists(rootPath))
        {
            yield break;
        }

        var pending = new Stack<string>();
        pending.Push(rootPath);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            foreach (var subDirectory in TryEnumerate(() => Directory.GetDirectories(current)))
            {
                pending.Push(subDirectory);
            }

            foreach (var file in TryEnumerate(() => Directory.GetFiles(current)))
            {
                if (TryClassify(file, out var type) && (!videosOnly || type == "视频"))
                {
                    yield return file;
                }
            }
        }
    }

    public string Classify(string path) => TryClassify(path, out var type) ? type : "其他";

    /// <summary>剪辑库分类：视频文件归为「视频剪辑」，其他类型返回空（不入库）</summary>
    public string ClassifyForClipLibrary(string path) =>
        TryClassify(path, out var type) && type == "视频" ? "视频剪辑" : string.Empty;

    private static bool TryClassify(string path, out string type)
    {
        var extension = Path.GetExtension(path);
        if (TextExtensions.Contains(extension))
        {
            type = "文本";
            return true;
        }

        if (ImageExtensions.Contains(extension))
        {
            type = "图片";
            return true;
        }

        if (VideoExtensions.Contains(extension))
        {
            type = "视频";
            return true;
        }

        if (AudioExtensions.Contains(extension))
        {
            type = "音频";
            return true;
        }

        type = string.Empty;
        return false;
    }

    private static IEnumerable<string> TryEnumerate(Func<IEnumerable<string>> factory)
    {
        try
        {
            return factory();
        }
        catch
        {
            return [];
        }
    }
}
