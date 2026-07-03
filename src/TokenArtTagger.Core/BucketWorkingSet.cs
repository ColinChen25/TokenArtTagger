namespace TokenArtTagger.Core;

public static class BucketWorkingSet
{
    public static IReadOnlyList<ImageItem> Filter(
        IReadOnlyCollection<ImageItem> items,
        BucketPass pass,
        BucketFilterMode mode)
    {
        return items.Where(item => Matches(item, pass, mode)).ToList();
    }

    private static bool Matches(ImageItem item, BucketPass pass, BucketFilterMode mode)
    {
        if (mode == BucketFilterMode.ChangedOnly)
        {
            return item.IsDirty;
        }

        if (mode == BucketFilterMode.ReadyToRename)
        {
            return RenameReadiness.Evaluate([item]).CanPreview;
        }

        var tags = item.CurrentTags;
        return pass switch
        {
            BucketPass.Gender => MatchValue(tags.Gender, TagSchema.IsKnownGender, mode),
            BucketPass.Role => MatchValue(tags.Role, TagSchema.IsKnownRole, mode),
            BucketPass.MeleeStyle => MatchStyle(tags, "melee", mode),
            BucketPass.RangeStyle => MatchStyle(tags, "range", mode),
            BucketPass.CasterStyle => MatchStyle(tags, "caster", mode),
            BucketPass.Race => MatchValue(tags.Race, TagSchema.IsKnownRace, mode),
            _ => false
        };
    }

    private static bool MatchValue(string? value, Func<string, bool> isKnown, BucketFilterMode mode)
    {
        return mode switch
        {
            BucketFilterMode.MissingOnly => string.IsNullOrWhiteSpace(value),
            BucketFilterMode.InvalidOnly => !string.IsNullOrWhiteSpace(value) && !isKnown(value),
            BucketFilterMode.AllForPass => true,
            _ => false
        };
    }

    private static bool MatchStyle(TagSet tags, string role, BucketFilterMode mode)
    {
        if (!string.Equals(tags.Role, role, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return mode switch
        {
            BucketFilterMode.MissingOnly => string.IsNullOrWhiteSpace(tags.WeaponOrStyle) ||
                !TagSchema.IsKnownStyleForRole(role, tags.WeaponOrStyle),
            BucketFilterMode.InvalidOnly => !string.IsNullOrWhiteSpace(tags.WeaponOrStyle) &&
                !TagSchema.IsKnownStyleForRole(role, tags.WeaponOrStyle),
            BucketFilterMode.AllForPass => true,
            _ => false
        };
    }
}
