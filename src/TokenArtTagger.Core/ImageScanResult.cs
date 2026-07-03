namespace TokenArtTagger.Core;

public sealed record ImageScanResult(IReadOnlyList<ImageItem> Items, IReadOnlyList<FileOperationError> Errors);

public sealed record FileOperationError(string Path, string Message);
