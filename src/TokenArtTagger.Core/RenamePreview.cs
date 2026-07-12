namespace TokenArtTagger.Core;

public sealed record RenamePreview(IReadOnlyList<RenamePreviewEntry> Entries)
{
    public bool CanRename => Entries.Count > 0 && Entries.All(entry => entry.CanRename);

    public int BlockingErrorCount => Entries.Count(entry => entry.HasBlockingError);

    public int AlreadyDesiredCount => Entries.Count(entry => entry.IsAlreadyDesiredHashFilename);
}

public sealed record RenamePreviewProgress(int Completed, int Total, string CurrentFileName);

public sealed record RenamePreviewEntry(
    ImageItem Item,
    string? ContentHash,
    string? ProposedFileName,
    string? ProposedPath,
    string? ErrorMessage)
{
    public bool CanRename => ErrorMessage is null && ProposedPath is not null;

    public bool IsAlreadyDesiredHashFilename => string.Equals(
        ErrorMessage,
        FileRenamer.AlreadyDesiredHashFilenameMessage,
        StringComparison.OrdinalIgnoreCase);

    public bool HasBlockingError => !string.IsNullOrWhiteSpace(ErrorMessage) && !IsAlreadyDesiredHashFilename;
}

public sealed record RenameBatchResult(int RenamedCount, IReadOnlyList<FileOperationError> Errors, string UndoLogPath);

public sealed record RenameUndoLog(DateTimeOffset CreatedAt, IReadOnlyList<RenameUndoEntry> Entries);

public sealed record RenameUndoEntry(string OriginalPath, string RenamedPath, string ContentHash);
