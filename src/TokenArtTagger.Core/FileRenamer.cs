using System.Text.Json;

namespace TokenArtTagger.Core;

public static class FileRenamer
{
    public static async Task<RenamePreview> BuildPreviewAsync(
        IReadOnlyList<ImageItem> items,
        IProgress<RenamePreviewProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var entries = new List<RenamePreviewEntry>();
        var completed = 0;

        foreach (var group in items.GroupBy(item => item.DirectoryPath, StringComparer.OrdinalIgnoreCase))
        {
            var existingNames = Directory.Exists(group.Key)
                ? Directory.EnumerateFiles(group.Key).Select(Path.GetFileName).Where(name => name is not null).ToHashSet(StringComparer.OrdinalIgnoreCase)!
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var selectedItem in group)
            {
                cancellationToken.ThrowIfCancellationRequested();
                existingNames.Remove(selectedItem.FileName);
                progress?.Report(new RenamePreviewProgress(completed, items.Count, selectedItem.FileName));

                try
                {
                    var validationErrors = FilenameGenerator.Validate(selectedItem.CurrentTags);
                    if (validationErrors.Count > 0)
                    {
                        entries.Add(new RenamePreviewEntry(selectedItem, null, null, null, string.Join("; ", validationErrors)));
                        continue;
                    }

                    var hash = await ContentHasher.HashFileHexAsync(selectedItem.FullPath, cancellationToken).ConfigureAwait(false);
                    var proposedFileName = FilenameGenerator.GenerateConflictSafe(
                        selectedItem.CurrentTags,
                        hash,
                        selectedItem.Extension,
                        existingNames);
                    var proposedPath = Path.Combine(selectedItem.DirectoryPath, proposedFileName);
                    if (string.Equals(proposedPath, selectedItem.FullPath, StringComparison.OrdinalIgnoreCase))
                    {
                        entries.Add(new RenamePreviewEntry(selectedItem, hash, proposedFileName, proposedPath, "already in desired hash filename format"));
                        continue;
                    }

                    existingNames.Add(proposedFileName);
                    entries.Add(new RenamePreviewEntry(selectedItem, hash, proposedFileName, proposedPath, null));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    entries.Add(new RenamePreviewEntry(selectedItem, null, null, null, ex.Message));
                }

                completed++;
                progress?.Report(new RenamePreviewProgress(completed, items.Count, selectedItem.FileName));
            }
        }

        return new RenamePreview(entries);
    }

    public static async Task<RenameBatchResult> RenameAsync(
        RenamePreview preview,
        string undoLogFolder,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<FileOperationError>();
        var undoEntries = new List<RenameUndoEntry>();

        foreach (var entry in preview.Entries.Where(entry => entry.CanRename))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (!string.Equals(
                    Path.GetDirectoryName(entry.Item.FullPath),
                    Path.GetDirectoryName(entry.ProposedPath),
                    StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add(new FileOperationError(entry.Item.FullPath, "Rename target must stay in the same folder."));
                    continue;
                }

                if (File.Exists(entry.ProposedPath))
                {
                    errors.Add(new FileOperationError(entry.Item.FullPath, $"Target filename already exists: {entry.ProposedFileName}"));
                    continue;
                }

                File.Move(entry.Item.FullPath, entry.ProposedPath!, overwrite: false);
                undoEntries.Add(new RenameUndoEntry(entry.Item.FullPath, entry.ProposedPath!, entry.ContentHash!));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add(new FileOperationError(entry.Item.FullPath, ex.Message));
            }
        }

        var undoLogPath = await WriteUndoLogAsync(undoLogFolder, undoEntries, cancellationToken).ConfigureAwait(false);
        return new RenameBatchResult(undoEntries.Count, errors, undoLogPath);
    }

    private static async Task<string> WriteUndoLogAsync(
        string undoLogFolder,
        IReadOnlyList<RenameUndoEntry> undoEntries,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(undoLogFolder);
        var fileName = $"rename-{DateTimeOffset.Now:yyyyMMdd-HHmmss-fff}.json";
        var undoLogPath = Path.Combine(undoLogFolder, fileName);
        var undoLog = new RenameUndoLog(DateTimeOffset.Now, undoEntries);
        var options = new JsonSerializerOptions { WriteIndented = true };

        await using var stream = File.Create(undoLogPath);
        await JsonSerializer.SerializeAsync(stream, undoLog, options, cancellationToken).ConfigureAwait(false);
        return undoLogPath;
    }
}
