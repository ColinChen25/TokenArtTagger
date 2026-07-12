using TokenArtTagger.Core;

namespace TokenArtTagger.Tests;

[TestClass]
public sealed class BucketShortcutMapTests
{
    [TestMethod]
    [DataRow(BucketPass.Gender, 1, TagSchema.GenderCategory, "male")]
    [DataRow(BucketPass.Gender, 2, TagSchema.GenderCategory, "female")]
    [DataRow(BucketPass.Role, 1, TagSchema.RoleCategory, "melee")]
    [DataRow(BucketPass.Role, 2, TagSchema.RoleCategory, "range")]
    [DataRow(BucketPass.Role, 3, TagSchema.RoleCategory, "caster")]
    [DataRow(BucketPass.Role, 4, TagSchema.RoleCategory, "generic")]
    [DataRow(BucketPass.MeleeStyle, 1, TagSchema.StyleCategory, "blade")]
    [DataRow(BucketPass.MeleeStyle, 2, TagSchema.StyleCategory, "polearm")]
    [DataRow(BucketPass.MeleeStyle, 3, TagSchema.StyleCategory, "dagger")]
    [DataRow(BucketPass.MeleeStyle, 4, TagSchema.StyleCategory, "unarmed")]
    [DataRow(BucketPass.RangeStyle, 1, TagSchema.StyleCategory, "bow")]
    [DataRow(BucketPass.RangeStyle, 2, TagSchema.StyleCategory, "crossbow")]
    [DataRow(BucketPass.RangeStyle, 3, TagSchema.StyleCategory, "gun")]
    [DataRow(BucketPass.CasterStyle, 1, TagSchema.StyleCategory, "wizard")]
    [DataRow(BucketPass.CasterStyle, 2, TagSchema.StyleCategory, "cleric")]
    [DataRow(BucketPass.CasterStyle, 3, TagSchema.StyleCategory, "bard")]
    [DataRow(BucketPass.CasterStyle, 4, TagSchema.StyleCategory, "druid")]
    [DataRow(BucketPass.Race, 1, TagSchema.RaceCategory, "human")]
    [DataRow(BucketPass.Race, 2, TagSchema.RaceCategory, "elf")]
    [DataRow(BucketPass.Race, 3, TagSchema.RaceCategory, "beastfolk")]
    [DataRow(BucketPass.Race, 4, TagSchema.RaceCategory, "dragon")]
    public void ForKey_ReturnsExpectedBucketShortcut(BucketPass pass, int key, string category, string value)
    {
        var shortcut = BucketShortcutMap.ForKey(pass, key);

        Assert.IsNotNull(shortcut);
        Assert.AreEqual(category, shortcut.Value.Category);
        Assert.AreEqual(value, shortcut.Value.Value);
    }

    [TestMethod]
    public void ForKey_ReturnsNullForUnmappedNumber()
    {
        Assert.IsNull(BucketShortcutMap.ForKey(BucketPass.Gender, 3));
        Assert.IsNull(BucketShortcutMap.ForKey(BucketPass.RangeStyle, 4));
        Assert.IsNull(BucketShortcutMap.ForKey(BucketPass.Race, 5));
    }

    [TestMethod]
    public void BucketDefinitionsExposeShortcutOrderFirst()
    {
        CollectionAssert.AreEqual(
            new[] { "blade", "polearm", "dagger", "unarmed" },
            BucketPassDefinition.For(BucketPass.MeleeStyle).Buckets.Take(4).Select(bucket => bucket.Value).ToArray());
        CollectionAssert.AreEqual(
            new[] { "bow", "crossbow", "gun", "thrown" },
            BucketPassDefinition.For(BucketPass.RangeStyle).Buckets.Take(4).Select(bucket => bucket.Value).ToArray());
        CollectionAssert.AreEqual(
            new[] { "human", "elf", "beastfolk", "dragon" },
            BucketPassDefinition.For(BucketPass.Race).Buckets.Take(4).Select(bucket => bucket.Value).ToArray());
    }
}
