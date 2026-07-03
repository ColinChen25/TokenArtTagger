namespace TokenArtTagger.Core;

public static class FileScanner
{
    public static Task<ImageScanResult> ScanAsync(string rootFolder, CancellationToken cancellationToken = default)
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

            foreach (var file in EnumerateFilesSafely(rootFolder, errors, cancellationToken))
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
        ICollection<FileOperationError> errors,
        CancellationToken cancellationToken)
    {
        IEnumerable<string> files = [];
        IEnumerable<string> directories = [];

        try
        {
            files = Directory.EnumerateFiles(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            errors.Add(new FileOperationError(folder, ex.Message));
        }

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return file;
        }

        try
        {
            directories = Directory.EnumerateDirectories(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            errors.Add(new FileOperationError(folder, ex.Message));
        }

        foreach (var directory in directories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var file in EnumerateFilesSafely(directory, errors, cancellationToken))
            {
                yield return file;
            }
        }
    }
}
