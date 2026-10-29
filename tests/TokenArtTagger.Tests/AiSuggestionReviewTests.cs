using TokenArtTagger.Core;

namespace TokenArtTagger.Tests;

[TestClass]
public sealed class AiSuggestionReviewTests
{
    private static AiReviewReceipt Receipt(string root)
    {
        var path = Path.Combine(root, "image.png");
        var sha = new string('a', 64);
        var run = new string('b', 64);
        var proposal = new AiSuggestion(AiSuggestion.Identity(path, sha, "fixture", "model", run), path, sha,
            new("female", "caster", "wizard", "elf"), "fixture", "model", run, "Fixture only", "identity", new(), DateTimeOffset.UtcNow);
        var plan = AiReviewPlan.KeepAll with { Gender = new(AiFieldAction.Suggestion), Race = new(AiFieldAction.Set, "human") };
        return new(1, proposal, "staged", new(), new(), plan, plan.Resolve(new(), proposal.Tags), false, DateTimeOffset.UtcNow);
    }

    [TestMethod]
    public void FieldsAreExplicitAndUnrequestedValuesStayUnresolved()
    {
        var before = new TagSet("male", "melee", "blade", "human");
        var ai = new TagSet("female", "caster", "wizard", "elf");
        Assert.AreEqual(before, AiReviewPlan.KeepAll.Resolve(before, ai));
        var mixed = AiReviewPlan.KeepAll with { Gender = new(AiFieldAction.Suggestion), Race = new(AiFieldAction.Clear) };
        Assert.AreEqual(new TagSet("female", "melee", "blade", null), mixed.Resolve(before, ai));
        Assert.IsTrue(AiReviewPlan.ReplacesExisting(before, before, mixed.Resolve(before, ai)));
        Assert.IsTrue(AiReviewPlan.ReplacesExisting(new(), before, new()));
        Assert.IsFalse(AiReviewPlan.ReplacesExisting(new(), new(), ai));
    }

    [TestMethod]
    public void RoleChangesNeverSilentlyClearStyle()
    {
        var before = new TagSet(Role: "range", WeaponOrStyle: "bow");
        var plan = AiReviewPlan.KeepAll with { Role = new(AiFieldAction.Set, "generic") };
        Assert.ThrowsExactly<InvalidDataException>(() => plan.Resolve(before, new()));
        Assert.AreEqual(new TagSet(Role: "generic"), (plan with { Style = new(AiFieldAction.Clear) }).Resolve(before, new()));
        Assert.ThrowsExactly<InvalidDataException>(() => (plan with { Gender = new(AiFieldAction.Set, "unknown") }).Resolve(new(), new()));
    }

    [TestMethod]
    public void ReceiptRequiresMatchingChoicesAndExplicitReplacement()
    {
        using var temp = new TempFolder();
        var r = Receipt(temp.Path);
        r.Validate();
        Assert.ThrowsExactly<InvalidDataException>(() => (r with { Before = new("male") }).Validate());
        (r with { Before = new("male"), ReplaceExistingConfirmed = true }).Validate();
        Assert.ThrowsExactly<InvalidDataException>(() => (r with { After = new("male") }).Validate());
        Assert.ThrowsExactly<InvalidDataException>(() => (r with { Outcome = "dismissed" }).Validate());
    }

    [TestMethod]
    public async Task ImmutableReceiptPersistsAndDuplicateNeverInvokesTagSave()
    {
        using var temp = new TempFolder();
        var r = Receipt(temp.Path);
        var store = new AiReviewStore(temp.Path);
        var applied = 0;
        await store.CommitAsync(r, () => { applied++; return Task.CompletedTask; });
        Assert.AreEqual(r, await new AiReviewStore(temp.Path).LoadAsync(r.Suggestion.Id));
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => store.CommitAsync(r, () => { applied++; return Task.CompletedTask; }));
        Assert.AreEqual(1, applied);
    }

    [TestMethod]
    public async Task FailedOrCancelledSaveLeavesNoReceiptAndCanRetry()
    {
        using var temp = new TempFolder();
        var r = Receipt(temp.Path);
        var store = new AiReviewStore(temp.Path);
        await Assert.ThrowsExactlyAsync<IOException>(() => store.CommitAsync(r, () => throw new IOException("fixture")));
        Assert.IsNull(await store.LoadAsync(r.Suggestion.Id));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => store.CommitAsync(r, () => throw new Exception("must not run"), cancelled.Token));
        Assert.IsNull(await store.LoadAsync(r.Suggestion.Id));
        await store.CommitAsync(r, () => Task.CompletedTask);
        Assert.IsNotNull(await store.LoadAsync(r.Suggestion.Id));
    }

    [TestMethod]
    public async Task ReceiptPublishFailureReportsThatTagsWereSaved()
    {
        using var temp = new TempFolder();
        var r = Receipt(temp.Path);
        var store = new AiReviewStore(temp.Path);
        var applied = false;
        await Assert.ThrowsExactlyAsync<AiReviewReceiptException>(() => store.CommitAsync(r, () =>
        {
            applied = true;
            Directory.CreateDirectory(Path.Combine(temp.Path, r.Suggestion.Id + ".json"));
            return Task.CompletedTask;
        }));
        Assert.IsTrue(applied);
        Assert.AreEqual(0, Directory.GetFiles(temp.Path, "*.tmp").Length);
    }

    [TestMethod]
    public async Task DismissalDoesNotNeedSourceFileOrPermitTagCallback()
    {
        using var temp = new TempFolder();
        var r = Receipt(temp.Path) with { Outcome = "dismissed", Decisions = AiReviewPlan.KeepAll, After = new() };
        var store = new AiReviewStore(temp.Path);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => store.CommitAsync(r, () => Task.CompletedTask));
        await store.CommitAsync(r);
        Assert.AreEqual("dismissed", (await store.LoadAsync(r.Suggestion.Id))!.Outcome);
    }

    [TestMethod]
    public async Task CorruptReceiptAndExternalWriterAreNotOverwritten()
    {
        using var temp = new TempFolder();
        var r = Receipt(temp.Path);
        var store = new AiReviewStore(temp.Path);
        using (var held = new FileStream(Path.Combine(temp.Path, "review.lock"), FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            await Assert.ThrowsExactlyAsync<IOException>(() => store.CommitAsync(r, () => throw new Exception("must not run")));
        var path = Path.Combine(temp.Path, r.Suggestion.Id + ".json");
        await File.WriteAllTextAsync(path, "{broken");
        await Assert.ThrowsAsync<System.Text.Json.JsonException>(() => store.LoadAsync(r.Suggestion.Id));
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => store.CommitAsync(r, () => throw new Exception("must not run")));
        Assert.AreEqual("{broken", await File.ReadAllTextAsync(path));
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => store.LoadAsync("../not-an-id"));
    }
}
