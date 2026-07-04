using TokenArtTagger.Core;

namespace TokenArtTagger.Tests;

[TestClass]
public sealed class ImageSearchMatcherTests
{
    [TestMethod]
    public void Matches_ReturnsTrueForEmptySearch()
    {
        var item = ImageItem.FromPath(Path.Combine(Path.GetTempPath(), "female-caster-bard-human.jpg"));

        Assert.IsTrue(ImageSearchMatcher.Matches(item, string.Empty));
    }

    [TestMethod]
    public void Matches_SearchesFilenameAndTagsWithoutTouchingFileContents()
    {
        var item = ImageItem.FromPath(Path.Combine(Path.GetTempPath(), "female-caster-bard-mermaid.jpg"));

        Assert.IsTrue(ImageSearchMatcher.Matches(item, "bard"));
        Assert.IsTrue(ImageSearchMatcher.Matches(item, "mermaid"));
        Assert.IsTrue(ImageSearchMatcher.Matches(item, "female-caster"));
        Assert.IsFalse(ImageSearchMatcher.Matches(item, "crossbow"));
    }
}
