namespace TokenArtTagger.Core;

public sealed record FileIdentity(string Key)
{
    public static FileIdentity FromPath(string path)
    {
        var info = new FileInfo(path);
        var fullPath = Path.GetFullPath(path).ToLowerInvariant();
        return new FileIdentity($"{fullPath}|{info.Length}|{info.LastWriteTimeUtc.Ticks}");
    }
}
