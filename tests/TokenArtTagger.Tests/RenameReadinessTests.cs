using TokenArtTagger.Core;

namespace TokenArtTagger.Tests;

[TestClass]
public sealed class RenameReadinessTests
{
    [TestMethod]
    public void Evaluate_AllowsGenericRoleWithoutStyle()
    {
        var item = CreateItem(new TagSet("female", "generic", null, "human"));

        var result = RenameReadiness.Evaluate([item]);

        Assert.IsTrue(result.CanPreview);
        Assert.AreEqual("", result.Message);
    }

    [TestMethod]
    public void Evaluate_BlocksNonGenericRoleWithoutStyle()
    {
        var item = CreateItem(new TagSet("female", "caster", null, "elf"));

        var result = RenameReadiness.Evaluate([item]);

        Assert.IsFalse(result.CanPreview);
        StringAssert.Contains(result.Message, "weapon/style");
    }

    [TestMethod]
    public void Evaluate_ExplainsMissingRace()
    {
        var item = CreateItem(new TagSet("male", "melee", "blade", null));

        var result = RenameReadiness.Evaluate([item]);

        Assert.IsFalse(result.CanPreview);
        StringAssert.Contains(result.Message, "race");
    }

    [TestMethod]
    public void Evaluate_BlocksEmptySelection()
    {
        var result = RenameReadiness.Evaluate([]);

        Assert.IsFalse(result.CanPreview);
        StringAssert.Contains(result.Message, "Select");
    }

    private static ImageItem CreateItem(TagSet tags)
    {
        var item = ImageItem.FromPath(Path.Combine(Path.GetTempPath(), "portrait.png"));
        return item with { CurrentTags = tags };
    }
}
