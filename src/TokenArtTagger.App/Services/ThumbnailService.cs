using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TokenArtTagger.App.Services;

public sealed class ThumbnailService
{
    private readonly ConcurrentDictionary<string, ImageSource?> _cache = new(StringComparer.OrdinalIgnoreCase);

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
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = decodePixelWidth;
                image.StreamSource = stream;
                image.EndInit();
                image.Freeze();
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

    public void Clear() => _cache.Clear();
}
