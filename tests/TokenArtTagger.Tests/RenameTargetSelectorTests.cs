using TokenArtTagger.Core;

namespace TokenArtTagger.Tests;

[TestClass]
public sealed class RenameTargetSelectorTests
{
    [TestMethod]
    public void Selected_ReturnsOnlySelectedItems()
    {
        var selected = CreateItem("selected.png", new TagSet("female", "generic", null, "human"));
        var unselected = CreateItem("unselected.png", new TagSet("male", "generic", null, "elf"));

        var targets = RenameTargetSelector.Selected([selected], [selected, unselected]);

        Assert.AreEqual(1, targets.Count);
        Assert.AreEqual("selected.png", targets[0].FileName);
    }

    [TestMethod]
    public void Dirty_ReturnsOnlyChangedItems()
    {
        var changed = CreateItem("changed.png", new TagSet(), new TagSet("female", "generic", null, "human"));
        var unchanged = CreateItem("unchanged.png", new TagSet("male", "generic", null, "elf"), new TagSet("male", "generic", null, "elf"));

        var targets = RenameTargetSelector.Dirty([changed, unchanged]);

        Assert.AreEqual(1, targets.Count);
        Assert.AreEqual("changed.png", targets[0].FileName);
    }

    private static ImageItem CreateItem(string fileName, TagSet currentTags)
    {
        return CreateItem(fileName, currentTags, currentTags);
    }

    private static ImageItem CreateItem(string fileName, TagSet parsedTags, TagSet currentTags)
    {
        var fullPath = Path.Combine(Path.GetTempPath(), fileName);
        return new ImageItem(fullPath, Path.GetTempPath(), fileName, Path.GetExtension(fileName), parsedTags, true)
        {
            CurrentTags = currentTags
        };
    }
}
