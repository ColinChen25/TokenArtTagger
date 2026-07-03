namespace TokenArtTagger.Core;

public static class TagIssue
{
    public static TagIssueResult Evaluate(TagSet tags)
    {
        var errors = FilenameGenerator.Validate(tags);
        if (errors.Count == 0)
        {
            return new TagIssueResult(TagIssueKind.None, string.Empty);
        }

        var kind = errors.Any(error => error.Contains("requires", StringComparison.OrdinalIgnoreCase) &&
                !error.Contains("weapon/style is required", StringComparison.OrdinalIgnoreCase))
            ? TagIssueKind.Invalid
            : TagIssueKind.Incomplete;

        return new TagIssueResult(kind, string.Join("; ", errors));
    }
}

public enum TagIssueKind
{
    None,
    Incomplete,
    Invalid
}

public sealed record TagIssueResult(TagIssueKind Kind, string Message)
{
    public bool BlocksRename => Kind is TagIssueKind.Incomplete or TagIssueKind.Invalid;
}
