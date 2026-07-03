namespace TokenArtTagger.Core;

public sealed record FilenameParseResult(
    string OriginalFileName,
    string BaseName,
    string Extension,
    TagSet Tags,
    bool IsRecognized);
