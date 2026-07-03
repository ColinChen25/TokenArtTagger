namespace TokenArtTagger.Core;

public sealed record ImageItem(
    string FullPath,
    string DirectoryPath,
    string FileName,
    string Extension,
    TagSet ParsedTags,
    bool IsRecognized)
{
    public TagSet CurrentTags { get; init; } = ParsedTags;

    public bool IsDirty => CurrentTags != ParsedTags;

    public static ImageItem FromPath(string path)
    {
        var fileName = Path.GetFileName(path);
        var parsed = FilenameParser.Parse(fileName);

        return new ImageItem(
            path,
            Path.GetDirectoryName(path) ?? string.Empty,
            fileName,
            parsed.Extension,
            parsed.Tags,
            parsed.IsRecognized);
    }
}
