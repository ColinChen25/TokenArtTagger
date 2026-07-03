namespace TokenArtTagger.Core;

public static class BucketRemainderTagger
{
    public static BucketRemainderResult ApplyDefault(
        IReadOnlyList<ImageItem> pageItems,
        BucketPass pass,
        string defaultValue)
    {
        var definition = BucketPassDefinition.For(pass);
        var updated = new List<ImageItem>(pageItems.Count);
        var changed = 0;

        foreach (var item in pageItems)
        {
            if (HasPassValue(item.CurrentTags, pass))
            {
                updated.Add(item);
                continue;
            }

            updated.Add(item with { CurrentTags = item.CurrentTags.WithTag(definition.Category, defaultValue) });
            changed++;
        }

        return new BucketRemainderResult(updated, changed);
    }

    private static bool HasPassValue(TagSet tags, BucketPass pass)
    {
        return pass switch
        {
            BucketPass.Gender => !string.IsNullOrWhiteSpace(tags.Gender),
            BucketPass.Role => !string.IsNullOrWhiteSpace(tags.Role),
            BucketPass.MeleeStyle or BucketPass.RangeStyle or BucketPass.CasterStyle => !string.IsNullOrWhiteSpace(tags.WeaponOrStyle),
            BucketPass.Race => !string.IsNullOrWhiteSpace(tags.Race),
            _ => false
        };
    }
}

public sealed record BucketRemainderResult(IReadOnlyList<ImageItem> Items, int ChangedCount);
