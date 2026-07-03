using TokenArtTagger.Core;

namespace TokenArtTagger.Tests;

[TestClass]
public sealed class WorkInProgressTagStoreTests
{
    [TestMethod]
    public async Task SaveAndLoadAsync_PersistsPartialTagsByFileIdentity()
    {
        using var temp = new TempFolder();
        var imagePath = Path.Combine(temp.Path, "sample.png");
        File.WriteAllBytes(imagePath, [1, 2, 3]);
        var storePath = Path.Combine(temp.Path, "wip.json");
        var identity = FileIdentity.FromPath(imagePath);
        var store = new WorkInProgressTagStore(storePath);

        await store.SaveAsync([new WorkInProgressTagRecord(identity.Key, new TagSet("female"))]);

        var loaded = await store.LoadAsync();

        Assert.AreEqual(1, loaded.Count);
        Assert.AreEqual("female", loaded[0].Tags.Gender);
    }

    [TestMethod]
    public async Task ClearAsync_RemovesStoredPartialTags()
    {
        using var temp = new TempFolder();
        var storePath = Path.Combine(temp.Path, "wip.json");
        var store = new WorkInProgressTagStore(storePath);

        await store.SaveAsync([new WorkInProgressTagRecord("sample", new TagSet("female"))]);
        await store.ClearAsync();

        var loaded = await store.LoadAsync();

        Assert.AreEqual(0, loaded.Count);
        Assert.IsFalse(File.Exists(storePath));
    }
}
