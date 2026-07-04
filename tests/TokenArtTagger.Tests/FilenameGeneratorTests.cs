using TokenArtTagger.Core;

namespace TokenArtTagger.Tests;

[TestClass]
public sealed class FilenameGeneratorTests
{
    [TestMethod]
    public void GenerateName_UsesFourPartNameForNonGenericRoleAndPreservesExtension()
    {
        var tags = new TagSet("female", "caster", "bard", "elf");

        var name = FilenameGenerator.Generate(tags, "91bc2299", ".JPG");

        Assert.AreEqual("female-caster-bard-elf__91bc22.JPG", name);
    }

    [TestMethod]
    public void GenerateName_OmitsStyleForGenericRole()
    {
        var tags = new TagSet("female", "generic", "wizard", "human");

        var name = FilenameGenerator.Generate(tags, "bb73aa99", ".webp");

        Assert.AreEqual("female-generic-human__bb73aa.webp", name);
    }

    [TestMethod]
    public void GenerateName_GeneratesElementalAndDruidTags()
    {
        var tags = new TagSet("female", "caster", "druid", "elemental");

        var name = FilenameGenerator.Generate(tags, "abcdef12", ".png");

        Assert.AreEqual("female-caster-druid-elemental__abcdef.png", name);
    }

    [TestMethod]
    public void GenerateName_RejectsMissingRequiredTags()
    {
        var tags = new TagSet("male", "melee", null, "dragon");

        var error = Assert.ThrowsExactly<InvalidOperationException>(() => FilenameGenerator.Generate(tags, "a2d93c44", ".png"));

        StringAssert.Contains(error.Message, "weapon/style");
    }

    [TestMethod]
    public void GenerateName_RejectsIncompatibleRoleAndStyle()
    {
        var tags = new TagSet("female", "range", "blade", "human");

        var error = Assert.ThrowsExactly<InvalidOperationException>(() => FilenameGenerator.Generate(tags, "a2d93c44", ".png"));

        StringAssert.Contains(error.Message, "range requires bow, gun, crossbow, or thrown");
    }

    [TestMethod]
    public void GenerateName_AllowsCasterDruid()
    {
        var tags = new TagSet("female", "caster", "druid", "human");

        var name = FilenameGenerator.Generate(tags, "a2d93c44", ".png");

        Assert.AreEqual("female-caster-druid-human__a2d93c.png", name);
    }

    [TestMethod]
    [DataRow("melee", "flail")]
    [DataRow("melee", "scythe")]
    [DataRow("melee", "blunt")]
    [DataRow("melee", "exotic")]
    [DataRow("melee", "rare")]
    [DataRow("range", "thrown")]
    [DataRow("caster", "druid")]
    public void GenerateName_AcceptsExpandedStyleVocabulary(string role, string style)
    {
        var tags = new TagSet("female", role, style, "chimera");

        var name = FilenameGenerator.Generate(tags, "a2d93c44", ".png");

        StringAssert.StartsWith(name, $"female-{role}-{style}-chimera__a2d93c");
    }

    [TestMethod]
    [DataRow("melee", "kama", "dagger")]
    [DataRow("melee", "chainsaw", "exotic")]
    [DataRow("range", "starknife", "thrown")]
    public void GenerateName_NormalizesStyleAliases(string role, string alias, string normalizedStyle)
    {
        var tags = new TagSet("female", role, alias, "human");

        var name = FilenameGenerator.Generate(tags, "a2d93c44", ".png");

        StringAssert.StartsWith(name, $"female-{role}-{normalizedStyle}-human__a2d93c");
    }

    [TestMethod]
    public void GenerateConflictSafeName_ExtendsHashWhenShortHashConflicts()
    {
        var tags = new TagSet("male", "range", "gun", "beastfolk");
        var existingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "male-range-gun-beastfolk__10f4ca.jfif"
        };

        var name = FilenameGenerator.GenerateConflictSafe(tags, "10f4ca8899aa", ".jfif", existingNames);

        Assert.AreEqual("male-range-gun-beastfolk__10f4ca8.jfif", name);
    }

    [TestMethod]
    public void ContentHash_IsStableForSameBytes()
    {
        var bytes = new byte[] { 1, 2, 3, 4, 5 };

        var first = ContentHasher.HashHex(bytes);
        var second = ContentHasher.HashHex(bytes);

        Assert.AreEqual(first, second);
        Assert.IsTrue(first.StartsWith("74f81f", StringComparison.OrdinalIgnoreCase));
    }
}
