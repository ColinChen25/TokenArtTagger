using System.Security.Cryptography;

namespace TokenArtTagger.Core;

public static class ContentHasher
{
    public static string HashHex(byte[] content)
    {
        var hash = SHA256.HashData(content);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static async Task<string> HashFileHexAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
