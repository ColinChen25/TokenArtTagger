namespace TokenArtTagger.Core;

public static class RenameReadiness
{
    public static RenameReadinessResult Evaluate(IReadOnlyCollection<ImageItem> items)
    {
        if (items.Count == 0)
        {
            return new RenameReadinessResult(false, "Select one or more images before previewing or renaming.");
        }

        var errors = items
            .SelectMany(item => FilenameGenerator.Validate(item.CurrentTags))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (errors.Count > 0)
        {
            return new RenameReadinessResult(false, string.Join("; ", errors));
        }

        return new RenameReadinessResult(true, string.Empty);
    }
}

public sealed record RenameReadinessResult(bool CanPreview, string Message);
