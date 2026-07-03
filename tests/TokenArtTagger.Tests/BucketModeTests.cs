using TokenArtTagger.Core;

namespace TokenArtTagger.Tests;

[TestClass]
public sealed class BucketModeTests
{
    [TestMethod]
    public void WorkingSet_GenderPassDefaultsToMissingGender()
    {
        var missing = CreateItem("missing.png", new TagSet(null, "melee", "blade", "human"));
        var tagged = CreateItem("tagged.png", new TagSet("female", "melee", "blade", "human"));

        var result = BucketWorkingSet.Filter([missing, tagged], BucketPass.Gender, BucketFilterMode.MissingOnly);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("missing.png", result[0].FileName);
    }

    [TestMethod]
    public void WorkingSet_RolePassDefaultsToMissingRole()
    {
        var missing = CreateItem("missing.png", new TagSet("female", null, null, "human"));
        var tagged = CreateItem("tagged.png", new TagSet("female", "caster", "bard", "human"));

        var result = BucketWorkingSet.Filter([missing, tagged], BucketPass.Role, BucketFilterMode.MissingOnly);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("missing.png", result[0].FileName);
    }

    [TestMethod]
    [DataRow(BucketPass.MeleeStyle, "melee")]
    [DataRow(BucketPass.RangeStyle, "range")]
    [DataRow(BucketPass.CasterStyle, "caster")]
    public void WorkingSet_StylePassDefaultsToMatchingRoleMissingStyle(BucketPass pass, string role)
    {
        var missing = CreateItem("missing.png", new TagSet("female", role, null, "human"));
        var otherRole = CreateItem("other.png", new TagSet("female", "generic", null, "human"));
        var complete = CreateItem("complete.png", new TagSet("female", role, TagSchema.StylesForRole(role)[0], "human"));

        var result = BucketWorkingSet.Filter([missing, otherRole, complete], pass, BucketFilterMode.MissingOnly);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("missing.png", result[0].FileName);
    }

    [TestMethod]
    public void WorkingSet_RacePassDefaultsToMissingRace()
    {
        var missing = CreateItem("missing.png", new TagSet("female", "generic", null, null));
        var tagged = CreateItem("tagged.png", new TagSet("female", "generic", null, "human"));

        var result = BucketWorkingSet.Filter([missing, tagged], BucketPass.Race, BucketFilterMode.MissingOnly);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("missing.png", result[0].FileName);
    }

    [TestMethod]
    public void ApplyDefaultToRemainder_AppliesOnlyCurrentPageItemsWithoutPassTag()
    {
        var page = new[]
        {
            CreateItem("one.png", new TagSet()),
            CreateItem("two.png", new TagSet("male")),
            CreateItem("three.png", new TagSet())
        };

        var result = BucketRemainderTagger.ApplyDefault(page, BucketPass.Gender, "female");

        Assert.AreEqual(2, result.ChangedCount);
        Assert.AreEqual("female", result.Items[0].CurrentTags.Gender);
        Assert.AreEqual("male", result.Items[1].CurrentTags.Gender);
        Assert.AreEqual("female", result.Items[2].CurrentTags.Gender);
    }

    [TestMethod]
    public void PartialTags_AreAllowedButNotRenameReady()
    {
        var item = CreateItem("partial.png", new TagSet("female"));

        var result = RenameReadiness.Evaluate([item]);

        Assert.IsFalse(result.CanPreview);
        StringAssert.Contains(result.Message, "role");
    }

    private static ImageItem CreateItem(string fileName, TagSet tags)
    {
        var fullPath = Path.Combine(Path.GetTempPath(), fileName);
        return new ImageItem(fullPath, Path.GetTempPath(), fileName, Path.GetExtension(fileName), new TagSet(), true)
        {
            CurrentTags = tags
        };
    }
}
