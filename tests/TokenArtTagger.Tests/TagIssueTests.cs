using TokenArtTagger.Core;

namespace TokenArtTagger.Tests;

[TestClass]
public sealed class TagIssueTests
{
    [TestMethod]
    public void Evaluate_DistinguishesIncompleteFromInvalidStyle()
    {
        var incomplete = TagIssue.Evaluate(new TagSet("female", "melee", null, "human"));
        var invalid = TagIssue.Evaluate(new TagSet("female", "range", "blade", "human"));

        Assert.AreEqual(TagIssueKind.Incomplete, incomplete.Kind);
        Assert.AreEqual(TagIssueKind.Invalid, invalid.Kind);
        StringAssert.Contains(invalid.Message, "range requires");
    }

    [TestMethod]
    public void Evaluate_TreatsCompleteGenericAsNone()
    {
        var result = TagIssue.Evaluate(new TagSet("male", "generic", "blade", "human"));

        Assert.AreEqual(TagIssueKind.None, result.Kind);
        Assert.AreEqual(string.Empty, result.Message);
    }
}
