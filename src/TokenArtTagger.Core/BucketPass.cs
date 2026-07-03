namespace TokenArtTagger.Core;

public enum BucketPass
{
    Gender,
    Role,
    MeleeStyle,
    RangeStyle,
    CasterStyle,
    Race
}

public enum BucketFilterMode
{
    MissingOnly,
    AllForPass,
    InvalidOnly,
    ChangedOnly,
    ReadyToRename
}

public sealed record BucketDefinition(string Category, string Value, string Label);

public sealed record BucketPassDefinition(
    BucketPass Pass,
    string DisplayName,
    string Category,
    IReadOnlyList<BucketDefinition> Buckets)
{
    public static BucketPassDefinition For(BucketPass pass)
    {
        return pass switch
        {
            BucketPass.Gender => new BucketPassDefinition(
                pass,
                "Gender",
                TagSchema.GenderCategory,
                TagSchema.Genders.Select(value => new BucketDefinition(TagSchema.GenderCategory, value, value)).ToList()),
            BucketPass.Role => new BucketPassDefinition(
                pass,
                "Role",
                TagSchema.RoleCategory,
                TagSchema.Roles.Select(value => new BucketDefinition(TagSchema.RoleCategory, value, value)).ToList()),
            BucketPass.MeleeStyle => StyleDefinition(pass, "Melee Weapon/Style", "melee", TagSchema.MeleeStyles),
            BucketPass.RangeStyle => StyleDefinition(pass, "Range Weapon/Style", "range", TagSchema.RangeStyles),
            BucketPass.CasterStyle => StyleDefinition(pass, "Caster Style", "caster", TagSchema.CasterStyles),
            BucketPass.Race => new BucketPassDefinition(
                pass,
                "Race",
                TagSchema.RaceCategory,
                TagSchema.Races.Select(value => new BucketDefinition(TagSchema.RaceCategory, value, value)).ToList()),
            _ => throw new ArgumentOutOfRangeException(nameof(pass), pass, null)
        };
    }

    private static BucketPassDefinition StyleDefinition(
        BucketPass pass,
        string displayName,
        string role,
        IReadOnlyList<string> styles)
    {
        return new BucketPassDefinition(
            pass,
            displayName,
            TagSchema.StyleCategory,
            styles.Select(value => new BucketDefinition(TagSchema.StyleCategory, value, value)).ToList());
    }
}
