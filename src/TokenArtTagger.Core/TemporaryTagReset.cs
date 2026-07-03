namespace TokenArtTagger.Core;

public static class TemporaryTagReset
{
    public static IReadOnlyList<ImageItem> Reset(IEnumerable<ImageItem> items)
    {
        return items.Select(item => item with { CurrentTags = item.ParsedTags }).ToList();
    }

    public static IReadOnlyList<ImageItem> ResetCurrentPass(
        IEnumerable<ImageItem> items,
        BucketPass pass,
        BucketFilterMode filterMode)
    {
        return Reset(BucketWorkingSet.Filter(items.ToList(), pass, filterMode));
    }
}
