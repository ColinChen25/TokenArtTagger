using TokenArtTagger.Core;

namespace TokenArtTagger.Tests;

[TestClass]
public sealed class TemporaryTagResetTests
{
    [TestMethod]
    public void Reset_RevertsSelectedItemsToParsedTagsWithoutChangingFiles()
    {
        var changed = Item("female-caster-bard-human.jpg") with
        {
            CurrentTags = new TagSet("male", "melee", "blade", "elf")
        };
        var unchanged = Item("male-generic-human.webp");

        var result = TemporaryTagReset.Reset([changed]);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(changed.ParsedTags, result[0].CurrentTags);
        Assert.AreEqual(changed.FullPath, result[0].FullPath);
        Assert.AreEqual(unchanged.CurrentTags, unchanged.ParsedTags);
    }

    [TestMethod]
    public void ResetCurrentPass_RevertsOnlyItemsShownForPass()
    {
        var genderChanged = Item("portrait.png") with
        {
            CurrentTags = new TagSet("female")
        };
        var roleChanged = Item("female-generic-human.webp") with
        {
            CurrentTags = new TagSet("female", "melee", "blade", "human")
        };

        var result = TemporaryTagReset.ResetCurrentPass([genderChanged, roleChanged], BucketPass.Role, BucketFilterMode.MissingOnly);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(genderChanged.ParsedTags, result[0].CurrentTags);
    }

    private static ImageItem Item(string fileName)
    {
        return ImageItem.FromPath(Path.Combine(Path.GetTempPath(), fileName));
    }
}
