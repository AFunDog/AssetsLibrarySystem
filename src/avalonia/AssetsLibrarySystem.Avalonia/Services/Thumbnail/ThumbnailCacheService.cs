using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Serilog;

namespace AssetsLibrarySystem.Avalonia.Services.Thumbnail;

/// <summary>
/// 缩略图缓存服务。
/// 为图片素材生成缩略图，视频/音频使用占位图标。
/// 内存 LRU 缓存最多 100 个缩略图。
/// </summary>
public sealed class ThumbnailCacheService
{
    private const int MaxCacheSize = 100;
    private const int ThumbnailMaxSide = 128;

    private readonly Dictionary<string, WeakReference<Bitmap>> _cache = new();
    private readonly Queue<string> _accessOrder = [];

    /// <summary>
    /// 获取素材的缩略图。如果缓存未命中，异步生成。
    /// 非图片格式返回 null（使用图标占位）。
    /// </summary>
    public async Task<Bitmap?> GetThumbnailAsync(string filePath, string assetType)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return null;

        // 只有图片生成缩略图
        if (assetType != "图片")
            return null;

        var cacheKey = filePath.ToUpperInvariant();

        // 检查缓存
        if (_cache.TryGetValue(cacheKey, out var weakRef) && weakRef.TryGetTarget(out var cachedBitmap))
        {
            Touch(cacheKey);
            return cachedBitmap;
        }

        // 异步生成缩略图
        try
        {
            var bitmap = await Task.Run(() => GenerateThumbnail(filePath));
            if (bitmap is not null)
            {
                // LRU 缓存管理
                if (_cache.Count >= MaxCacheSize)
                    EvictOldest();

                _cache[cacheKey] = new WeakReference<Bitmap>(bitmap);
                _accessOrder.Enqueue(cacheKey);
                Log.Debug("缩略图已生成: path={Path}", filePath);
            }
            return bitmap;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "缩略图生成失败: path={Path}", filePath);
            return null;
        }
    }

    /// <summary>清除缓存</summary>
    public void ClearCache()
    {
        _cache.Clear();
        _accessOrder.Clear();
    }

    private static Bitmap? GenerateThumbnail(string filePath)
    {
        try
        {
            var source = new Bitmap(filePath);
            var scale = Math.Min(
                (double)ThumbnailMaxSide / source.PixelSize.Width,
                (double)ThumbnailMaxSide / source.PixelSize.Height);

            if (scale >= 1.0)
                return source; // 原图小于缩略图尺寸

            var newWidth = (int)(source.PixelSize.Width * scale);
            var newHeight = (int)(source.PixelSize.Height * scale);

            // 使用 CreateScaledBitmap 创建缩略图
            var thumbnail = source.CreateScaledBitmap(
                new global::Avalonia.PixelSize(newWidth, newHeight),
                BitmapInterpolationMode.HighQuality);
            source.Dispose();
            return thumbnail;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "无法生成缩略图: path={Path}", filePath);
            return null;
        }
    }

    private void Touch(string key)
    {
        // 移到访问队列末尾
        var tempList = new List<string>(_accessOrder);
        tempList.Remove(key);
        tempList.Add(key);
        _accessOrder.Clear();
        foreach (var k in tempList)
            _accessOrder.Enqueue(k);
    }

    private void EvictOldest()
    {
        while (_accessOrder.Count > 0)
        {
            var oldest = _accessOrder.Dequeue();
            if (_cache.Remove(oldest))
                break;
        }
    }
}