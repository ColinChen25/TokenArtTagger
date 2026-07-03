namespace TokenArtTagger.Core;

public static class UndoLogService
{
    public const string LegacyUndoFolderName = ".tokenarttagger-undo";

    public static string DefaultUndoLogFolder()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TokenArtTagger",
            "UndoLogs");
    }

    public static bool HasLegacyUndoFolder(string rootFolder)
    {
        return Directory.Exists(Path.Combine(rootFolder, LegacyUndoFolderName));
    }
}
