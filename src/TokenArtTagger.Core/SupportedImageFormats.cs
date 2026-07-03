namespace TokenArtTagger.Core;

public static class SupportedImageFormats
{
    public static readonly IReadOnlySet<string> Extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",
        ".jpeg",
        ".jfif",
        ".png",
        ".webp",
        ".gif"
    };

    public static bool IsSupported(string path)
    {
        return Extensions.Contains(Path.GetExtension(path));
    }
}
