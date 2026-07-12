namespace TokenArtTagger.Core;

public static class BucketShortcutMap
{
    public static IReadOnlyList<BucketShortcut> ForPass(BucketPass pass)
    {
        return pass switch
        {
            BucketPass.Gender =>
            [
                new BucketShortcut(1, TagSchema.GenderCategory, "male"),
                new BucketShortcut(2, TagSchema.GenderCategory, "female")
            ],
            BucketPass.Role =>
            [
                new BucketShortcut(1, TagSchema.RoleCategory, "melee"),
                new BucketShortcut(2, TagSchema.RoleCategory, "range"),
                new BucketShortcut(3, TagSchema.RoleCategory, "caster"),
                new BucketShortcut(4, TagSchema.RoleCategory, "generic")
            ],
            BucketPass.MeleeStyle =>
            [
                new BucketShortcut(1, TagSchema.StyleCategory, "blade"),
                new BucketShortcut(2, TagSchema.StyleCategory, "polearm"),
                new BucketShortcut(3, TagSchema.StyleCategory, "dagger"),
                new BucketShortcut(4, TagSchema.StyleCategory, "unarmed")
            ],
            BucketPass.RangeStyle =>
            [
                new BucketShortcut(1, TagSchema.StyleCategory, "bow"),
                new BucketShortcut(2, TagSchema.StyleCategory, "crossbow"),
                new BucketShortcut(3, TagSchema.StyleCategory, "gun")
            ],
            BucketPass.CasterStyle =>
            [
                new BucketShortcut(1, TagSchema.StyleCategory, "wizard"),
                new BucketShortcut(2, TagSchema.StyleCategory, "cleric"),
                new BucketShortcut(3, TagSchema.StyleCategory, "bard"),
                new BucketShortcut(4, TagSchema.StyleCategory, "druid")
            ],
            BucketPass.Race =>
            [
                new BucketShortcut(1, TagSchema.RaceCategory, "human"),
                new BucketShortcut(2, TagSchema.RaceCategory, "elf"),
                new BucketShortcut(3, TagSchema.RaceCategory, "beastfolk"),
                new BucketShortcut(4, TagSchema.RaceCategory, "dragon")
            ],
            _ => []
        };
    }

    public static BucketShortcut? ForKey(BucketPass pass, int key)
    {
        return ForPass(pass)
            .Where(shortcut => shortcut.Key == key)
            .Select(shortcut => (BucketShortcut?)shortcut)
            .FirstOrDefault();
    }

    public static int? KeyFor(BucketPass pass, string category, string value)
    {
        return ForPass(pass)
            .Where(candidate =>
                string.Equals(candidate.Category, category, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(candidate.Value, value, StringComparison.OrdinalIgnoreCase))
            .Select(shortcut => (int?)shortcut.Key)
            .FirstOrDefault();
    }
}

public readonly record struct BucketShortcut(int Key, string Category, string Value);
