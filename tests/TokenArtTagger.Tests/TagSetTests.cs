using TokenArtTagger.Core;

namespace TokenArtTagger.Tests;

[TestClass]
public sealed class TagSetTests
{
    [TestMethod]
    public void WithTag_ClearsIncompatibleStyleWhenRoleChanges()
    {
        var tags = new TagSet("female", "melee", "blade", "human");

        var updated = tags.WithTag(TagSchema.RoleCategory, "range");

        Assert.AreEqual("range", updated.Role);
        Assert.IsNull(updated.WeaponOrStyle);
    }

    [TestMethod]
    public void WithTag_KeepsCompatibleStyleWhenRoleChanges()
    {
        var tags = new TagSet("female", "melee", "blade", "human");

        var updated = tags.WithTag(TagSchema.RoleCategory, "melee");

        Assert.AreEqual("blade", updated.WeaponOrStyle);
    }
}
