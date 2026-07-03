using System.Text.Json;
using TokenArtTagger.Core;

namespace TokenArtTagger.Tests;

[TestClass]
public sealed class FileRenamerTests
{
    [TestMethod]
    public async Task BuildPreviewAsync_ReturnsValidationErrorWhenStyleIsMissing()
    {
        using var temp = new TempFolder();
        var path = Path.Combine(temp.Path, "portrait.png");
        File.WriteAllBytes(path, [1, 2, 3]);
        var item = ImageItem.FromPath(path) with
        {
            CurrentTags = new TagSet("male", "melee", null, "dragon")
        };

        var preview = await FileRenamer.BuildPreviewAsync([item]);

        Assert.AreEqual(1, preview.Entries.Count);
        Assert.IsFalse(preview.CanRename);
        StringAssert.Contains(preview.Entries[0].ErrorMessage!, "weapon/style");
    }

    [TestMethod]
    public async Task BuildPreviewAsync_UsesLongerHashWhenSixCharacterNameExists()
    {
        using var temp = new TempFolder();
        var path = Path.Combine(temp.Path, "old.jfif");
        File.WriteAllBytes(path, [1, 2, 3, 4, 5]);
        File.WriteAllBytes(Path.Combine(temp.Path, "male-range-gun-beastfolk__74f81f.jfif"), [9]);
        var item = ImageItem.FromPath(path) with
        {
            CurrentTags = new TagSet("male", "range", "gun", "beastfolk")
        };

        var preview = await FileRenamer.BuildPreviewAsync([item]);

        Assert.IsTrue(preview.CanRename);
        Assert.AreEqual("male-range-gun-beastfolk__74f81fe.jfif", preview.Entries[0].ProposedFileName);
    }

    [TestMethod]
    public async Task BuildPreviewAsync_NormalizesParsedNumberedFilenameWithoutTagChanges()
    {
        using var temp = new TempFolder();
        var path = Path.Combine(temp.Path, "female-caster-bard-human (036).jpg");
        File.WriteAllBytes(path, [1, 2, 3, 4, 5]);
        var item = ImageItem.FromPath(path);

        var preview = await FileRenamer.BuildPreviewAsync([item]);

        Assert.IsTrue(preview.CanRename);
        Assert.AreEqual("female-caster-bard-human__74f81f.jpg", preview.Entries[0].ProposedFileName);
    }

    [TestMethod]
    public async Task BuildPreviewAsync_AllowsGenericRoleWithoutStyle()
    {
        using var temp = new TempFolder();
        var path = Path.Combine(temp.Path, "portrait.webp");
        File.WriteAllBytes(path, [1, 2, 3, 4, 5]);
        var item = ImageItem.FromPath(path) with
        {
            CurrentTags = new TagSet("male", "generic", null, "human")
        };

        var preview = await FileRenamer.BuildPreviewAsync([item]);

        Assert.IsTrue(preview.CanRename);
        Assert.AreEqual("male-generic-human__74f81f.webp", preview.Entries[0].ProposedFileName);
    }

    [TestMethod]
    public async Task RenameAsync_RenamesInPlaceAndWritesUndoLog()
    {
        using var temp = new TempFolder();
        var path = Path.Combine(temp.Path, "female-caster-bard-elf (840).JPG");
        File.WriteAllBytes(path, [1, 2, 3, 4, 5]);
        var item = ImageItem.FromPath(path);
        var preview = await FileRenamer.BuildPreviewAsync([item]);

        var result = await FileRenamer.RenameAsync(preview, temp.Path);

        Assert.AreEqual(1, result.RenamedCount);
        Assert.AreEqual(0, result.Errors.Count);
        Assert.IsFalse(File.Exists(path));
        Assert.IsTrue(File.Exists(Path.Combine(temp.Path, "female-caster-bard-elf__74f81f.JPG")));
        Assert.IsTrue(File.Exists(result.UndoLogPath));

        using var log = File.OpenRead(result.UndoLogPath);
        var undoLog = await JsonSerializer.DeserializeAsync<RenameUndoLog>(log);

        Assert.IsNotNull(undoLog);
        Assert.AreEqual(1, undoLog.Entries.Count);
        Assert.AreEqual(path, undoLog.Entries[0].OriginalPath);
    }
}
