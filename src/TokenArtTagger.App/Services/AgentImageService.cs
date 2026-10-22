using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TokenArtTagger.App.Services;

public static class AgentImageService
{
    public static Task<AgentImage> ReadAsync(string path, int maxDimension, CancellationToken token) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > 64 * 1024 * 1024) throw new IOException("Image exceeds the 64 MB agent input limit.");
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
        var first = decoder.Frames[0];
        if ((long)first.PixelWidth * first.PixelHeight > 80_000_000) throw new IOException("Image exceeds the 80 megapixel agent decode limit.");
        stream.Position = 0;
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        if (first.PixelWidth >= first.PixelHeight) bitmap.DecodePixelWidth = Math.Min(first.PixelWidth, maxDimension);
        else bitmap.DecodePixelHeight = Math.Min(first.PixelHeight, maxDimension);
        bitmap.EndInit();
        bitmap.Freeze();
        token.ThrowIfCancellationRequested();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = new MemoryStream();
        encoder.Save(output);
        if (output.Length > 8 * 1024 * 1024) throw new IOException("Encoded image exceeds agent response limit.");
        return new AgentImage(Convert.ToBase64String(output.ToArray()), bitmap.PixelWidth, bitmap.PixelHeight);
    }, token);
}
public sealed record AgentImage(string Base64, int Width, int Height);
