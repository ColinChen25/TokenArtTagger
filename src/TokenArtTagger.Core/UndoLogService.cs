namespace TokenArtTagger.Core;

public static class UndoLogService
{
    public const string LegacyUndoFolderName = ".tokenarttagger-undo";

    public static string DefaultUndoLogFolder()
    {
        return Path.Combine(
            AppDataPaths.Root,
            "UndoLogs");
    }

    public static bool HasLegacyUndoFolder(string rootFolder)
    {
        return Directory.Exists(Path.Combine(rootFolder, LegacyUndoFolderName));
    }
}
