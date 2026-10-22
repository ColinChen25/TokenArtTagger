namespace TokenArtTagger.Core;

public static class AppDataPaths
{
    // Allows integration tests and independent app profiles to avoid touching normal user state.
    public static string Root => Environment.GetEnvironmentVariable("TOKENARTTAGGER_DATA_DIR") is { Length: > 0 } custom
        ? Path.GetFullPath(custom)
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TokenArtTagger");
}
