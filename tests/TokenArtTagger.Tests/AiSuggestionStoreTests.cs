using System.Text.Json;
using TokenArtTagger.Core;

namespace TokenArtTagger.Tests;

[TestClass]
public sealed class AiSuggestionStoreTests
{
    private static AiSuggestion Proposal(string directory, string name = "image.png", string run = "a")
    {
        var path = Path.Combine(directory, name);
        var sha = new string('b', 64);
        var fingerprint = new string(run[0], 64);
        return new(AiSuggestion.Identity(path, sha, "fixture", "local-model", fingerprint), path, sha,
            new TagSet("female", "caster", null, null), "fixture", "local-model", fingerprint, "Visible robe",
            path + "|1|1", new TagSet(), DateTimeOffset.UtcNow);
    }

    [TestMethod]
    public async Task PartialProposal_PersistsWithoutTouchingImageOrTemporaryTags()
    {
        using var temp = new TempFolder();
        var row = Proposal(temp.Path);
        File.WriteAllBytes(row.ImagePath, [1, 2, 3]);
        var wip = new WorkInProgressTagStore(Path.Combine(temp.Path, "wip.json"));
        await wip.SaveAsync([new("manual", new TagSet("male"))]);
        var previous = File.ReadAllBytes(Path.Combine(temp.Path, "wip.json"));
        var store = new AiSuggestionStore(Path.Combine(temp.Path, "queue.json"));
        Assert.AreEqual(1, await store.AddAsync([row]));
        Assert.AreEqual(row, (await new AiSuggestionStore(Path.Combine(temp.Path, "queue.json")).LoadAsync()).Single());
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, File.ReadAllBytes(row.ImagePath));
        CollectionAssert.AreEqual(previous, File.ReadAllBytes(Path.Combine(temp.Path, "wip.json")));
    }

    [TestMethod]
    public async Task Retry_PreservesOriginalImportSnapshotAndDoesNotDuplicate()
    {
        using var temp = new TempFolder();
        var row = Proposal(temp.Path);
        var store = new AiSuggestionStore(Path.Combine(temp.Path, "queue.json"));
        await store.AddAsync([row]);
        Assert.AreEqual(0, await store.AddAsync([row with { ImportedUtc = row.ImportedUtc.AddDays(1), TagsAtImport = new TagSet("male") }]));
        Assert.AreEqual(row, (await store.LoadAsync()).Single());
    }

    [TestMethod]
    public async Task ConflictingRetry_RejectsEntireBatchAndRetainsEarlierQueue()
    {
        using var temp = new TempFolder();
        var row = Proposal(temp.Path);
        var store = new AiSuggestionStore(Path.Combine(temp.Path, "queue.json"));
        await store.AddAsync([row]);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.AddAsync([
            Proposal(temp.Path, "second.png"), row with { Tags = new TagSet("male") }]));
        Assert.AreEqual(row, (await store.LoadAsync()).Single());
    }

    [TestMethod]
    public async Task SeparateLocationsAndRuns_KeepDistinctProposalsForSamePixels()
    {
        using var temp = new TempFolder();
        var store = new AiSuggestionStore(Path.Combine(temp.Path, "queue.json"));
        Assert.AreEqual(3, await store.AddAsync([Proposal(temp.Path), Proposal(temp.Path, "copy.png"), Proposal(temp.Path, run: "c")]));
        Assert.AreEqual(3, (await store.LoadAsync()).Count);
    }

    [TestMethod]
    public async Task InvalidTagsIdentityAndDuplicateBatch_AreRejectedBeforeWrite()
    {
        using var temp = new TempFolder();
        var row = Proposal(temp.Path);
        var path = Path.Combine(temp.Path, "queue.json");
        var store = new AiSuggestionStore(path);
        foreach (var bad in new[] { row with { Id = "wrong" }, row with { Sha256 = "bad" },
                     row with { Tags = new TagSet(Role: "generic", WeaponOrStyle: "bow") },
                     row with { Tags = new TagSet(Race: "invented") }, row with { Evidence = new string('x', 2001) } })
            await Assert.ThrowsAsync<InvalidDataException>(() => store.AddAsync([bad]));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.AddAsync([row, row]));
        Assert.IsFalse(File.Exists(path));
        Assert.AreEqual(1, await store.AddAsync([row with { Tags = new TagSet(Role: "generic") }]));
    }

    [TestMethod]
    public async Task CancelledWriteAndExternalLock_PreserveQueue()
    {
        using var temp = new TempFolder();
        var path = Path.Combine(temp.Path, "queue.json");
        var store = new AiSuggestionStore(path);
        var row = Proposal(temp.Path);
        await store.AddAsync([row]);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => store.AddAsync([Proposal(temp.Path, "second.png")], cancelled.Token));
        using (var held = new FileStream(path + ".lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            await Assert.ThrowsAsync<IOException>(() => new AiSuggestionStore(path).AddAsync([Proposal(temp.Path, "second.png")]));
        Assert.AreEqual(row, (await store.LoadAsync()).Single());
    }

    [TestMethod]
    public async Task ConcurrentStores_MergeWithoutLosingUnrelatedProposals()
    {
        using var temp = new TempFolder();
        var path = Path.Combine(temp.Path, "queue.json");
        await Task.WhenAll(Enumerable.Range(0, 12).Select(i => new AiSuggestionStore(path).AddAsync([Proposal(temp.Path, $"{i}.png")])));
        Assert.AreEqual(12, (await new AiSuggestionStore(path).LoadAsync()).Count);
    }

    [TestMethod]
    public async Task CorruptOrOversizedStore_IsNotSilentlyReplaced()
    {
        using var temp = new TempFolder();
        var path = Path.Combine(temp.Path, "queue.json");
        var store = new AiSuggestionStore(path);
        File.WriteAllText(path, "{\"version\":2,\"suggestions\":[]}");
        await Assert.ThrowsAsync<InvalidDataException>(() => store.AddAsync([Proposal(temp.Path)]));
        Assert.IsTrue(File.ReadAllText(path).Contains("\"version\":2"));
        File.WriteAllText(path, "{broken");
        await Assert.ThrowsAsync<JsonException>(() => store.LoadAsync());
        using (var file = new FileStream(path, FileMode.Create, FileAccess.Write)) file.SetLength(16 * 1024 * 1024 + 1);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.LoadAsync());
    }
}
