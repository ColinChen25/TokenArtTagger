using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TokenArtTagger.App.Services;

public sealed class ThumbnailService
{
    private readonly ConcurrentDictionary<string, ImageSource?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _cacheFolder;

    public ThumbnailService()
    {
        _cacheFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TokenArtTagger",
            "thumbnail-cache");
        Directory.CreateDirectory(_cacheFolder);
    }

    public Task<ImageSource?> LoadThumbnailAsync(string path, int decodePixelWidth = 180)
    {
        if (_cache.TryGetValue(path, out var cached))
        {
            return Task.FromResult(cached);
        }

        return Task.Run(() =>
        {
            try
            {
                var cachePath = GetCachePath(path, decodePixelWidth);
                var image = File.Exists(cachePath)
                    ? LoadBitmap(cachePath, decodePixelWidth)
                    : LoadAndCacheOriginal(path, cachePath, decodePixelWidth);

                _cache[path] = image;
                return (ImageSource?)image;
            }
            catch
            {
                _cache[path] = null;
                return null;
            }
        });
    }

    public void ClearMemory() => _cache.Clear();

    private BitmapSource LoadAndCacheOriginal(string path, string cachePath, int decodePixelWidth)
    {
        var image = LoadBitmap(path, decodePixelWidth);

        try
        {
            SavePng(image, cachePath);
        }
        catch
        {
            // Cache writes are best-effort; thumbnail display should still work.
        }

        return image;
    }

    private static BitmapImage LoadBitmap(string path, int decodePixelWidth)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.DecodePixelWidth = decodePixelWidth;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private static void SavePng(BitmapSource image, string cachePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));

        using var output = new FileStream(cachePath, FileMode.Create, FileAccess.Write, FileShare.None);
        encoder.Save(output);
    }

    private string GetCachePath(string path, int decodePixelWidth)
    {
        var info = new FileInfo(path);
        var metadata = $"{Path.GetFullPath(path)}|{info.Length}|{info.LastWriteTimeUtc.Ticks}|{decodePixelWidth}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(metadata))).ToLowerInvariant();
        return Path.Combine(_cacheFolder, $"{hash}.png");
    }
}
