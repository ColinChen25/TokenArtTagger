using TokenArtTagger.Core;

namespace TokenArtTagger.Tests;

[TestClass]
public sealed class RenamePreviewSessionTests
{
    [TestMethod]
    public void RenameSelectedWithoutPreviewCreatesSelectedOnlyPreviewState()
    {
        var selected = CreateItem("selected.png", new TagSet("female", "generic", null, "human"));
        var unselected = CreateItem("unselected.png", new TagSet("male", "generic", null, "human"));
        var session = new RenamePreviewSession();

        var targets = session.TargetsForRenameSelectedWithoutPreview([selected], [selected, unselected]);

        Assert.AreEqual(1, targets.Count);
        Assert.AreEqual("selected.png", targets[0].FileName);
        Assert.AreEqual(RenamePreviewScope.Selected, session.Scope);
        Assert.IsFalse(session.CanConfirmRename);
    }

    private static ImageItem CreateItem(string fileName, TagSet tags)
    {
        var fullPath = Path.Combine(Path.GetTempPath(), fileName);
        return new ImageItem(fullPath, Path.GetTempPath(), fileName, Path.GetExtension(fileName), tags, true)
        {
            CurrentTags = tags
        };
    }
}
