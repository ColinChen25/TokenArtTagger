using TokenArtTagger.Core;

namespace TokenArtTagger.Tests;

[TestClass]
public sealed class BucketWorkingSessionTests
{
    [TestMethod]
    public void Materialize_KeepsCompletedMissingOnlyItemsUntilSessionIsRebuilt()
    {
        var missing = CreateItem("missing.png", new TagSet("female", "generic", null, null));
        var complete = CreateItem("complete.png", new TagSet("female", "generic", null, "human"));
        var session = BucketWorkingSession.Create([missing, complete], BucketPass.Race, BucketFilterMode.MissingOnly);

        var updatedMissing = missing with { CurrentTags = missing.CurrentTags with { Race = "elf" } };
        var visible = session.Materialize([updatedMissing, complete], string.Empty);

        Assert.AreEqual(1, visible.Count);
        Assert.AreEqual("missing.png", visible[0].FileName);
        Assert.AreEqual(1, session.CompletedThisPassCount([updatedMissing, complete]));
    }

    [TestMethod]
    public void Materialize_SearchFiltersWithinStableWorkingSetWithoutChangingMembership()
    {
        var first = CreateItem("female-caster-bard-human.png", new TagSet("female", "caster", "bard", null));
        var second = CreateItem("male-caster-cleric-elf.png", new TagSet("male", "caster", "cleric", null));
        var outside = CreateItem("complete.png", new TagSet("female", "caster", "bard", "human"));
        var session = BucketWorkingSession.Create([first, second, outside], BucketPass.Race, BucketFilterMode.MissingOnly);

        var visible = session.Materialize([first, second, outside], "cleric");

        Assert.AreEqual(2, session.Count);
        Assert.AreEqual(1, visible.Count);
        Assert.AreEqual("male-caster-cleric-elf.png", visible[0].FileName);
    }

    [TestMethod]
    public void Create_RebuildsMembershipForDifferentPassOrFilter()
    {
        var missingRace = CreateItem("missing-race.png", new TagSet("female", "generic", null, null));
        var missingRole = CreateItem("missing-role.png", new TagSet("female", null, null, "human"));
        var items = new[] { missingRace, missingRole };

        var raceSession = BucketWorkingSession.Create(items, BucketPass.Race, BucketFilterMode.MissingOnly);
        var roleSession = BucketWorkingSession.Create(items, BucketPass.Role, BucketFilterMode.MissingOnly);
        var allRaceSession = BucketWorkingSession.Create(items, BucketPass.Race, BucketFilterMode.AllForPass);

        CollectionAssert.AreEqual(new[] { "missing-race.png" }, raceSession.Materialize(items, string.Empty).Select(item => item.FileName).ToArray());
        CollectionAssert.AreEqual(new[] { "missing-role.png" }, roleSession.Materialize(items, string.Empty).Select(item => item.FileName).ToArray());
        Assert.AreEqual(2, allRaceSession.Count);
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
