namespace TokenArtTagger.Core;

public static class FileScanner
{
    public static Task<ImageScanResult> ScanAsync(string rootFolder, CancellationToken cancellationToken = default)
        => ScanAsync(rootFolder, true, [], cancellationToken);

    public static Task<ImageScanResult> ScanAsync(string rootFolder, bool recursive, IReadOnlyCollection<string> excludedDirectories, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var items = new List<ImageItem>();
            var errors = new List<FileOperationError>();

            if (!Directory.Exists(rootFolder))
            {
                errors.Add(new FileOperationError(rootFolder, "Folder does not exist."));
                return new ImageScanResult(items, errors);
            }

            var exclusions = excludedDirectories.Select(Path.GetFullPath).Select(p => p.TrimEnd(Path.DirectorySeparatorChar)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var file in EnumerateFilesSafely(Path.GetFullPath(rootFolder), recursive, exclusions, errors, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!SupportedImageFormats.IsSupported(file))
                {
                    continue;
                }

                try
                {
                    items.Add(ImageItem.FromPath(file));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
                {
                    errors.Add(new FileOperationError(file, ex.Message));
                }
            }

            return new ImageScanResult(items, errors);
        }, cancellationToken);
    }

    private static IEnumerable<string> EnumerateFilesSafely(
        string folder,
        bool recursive,
        IReadOnlySet<string> exclusions,
        ICollection<FileOperationError> errors,
        CancellationToken cancellationToken)
    {
        if (exclusions.Contains(folder.TrimEnd(Path.DirectorySeparatorChar))) yield break;
        IEnumerable<string> files = [];
        IEnumerable<string> directories = [];

        try
        {
            files = Directory.GetFiles(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            errors.Add(new FileOperationError(folder, ex.Message));
        }

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool isLink;
            try { isLink = (File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { errors.Add(new FileOperationError(file, ex.Message)); continue; }
            if (isLink) continue;
            yield return file;
        }

        if (!recursive) yield break;

        try
        {
            directories = Directory.GetDirectories(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            errors.Add(new FileOperationError(folder, ex.Message));
        }

        foreach (var directory in directories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool isLink;
            try { isLink = (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { errors.Add(new FileOperationError(directory, ex.Message)); continue; }
            if (isLink) continue;
            foreach (var file in EnumerateFilesSafely(directory, recursive, exclusions, errors, cancellationToken))
            {
                yield return file;
            }
        }
    }
}
