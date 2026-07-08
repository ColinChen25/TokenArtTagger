using TokenArtTagger.Core;

namespace TokenArtTagger.Tests;

[TestClass]
public sealed class DragSelectionTests
{
    [TestMethod]
    public void PayloadFromAlreadySelectedOriginPreservesWholeSelection()
    {
        var selected = new[] { "one", "two", "three" };

        var payload = DragSelection.PayloadForDragStart(selected, "two", originAlreadySelected: true);

        CollectionAssert.AreEqual(selected, payload.ToArray());
    }

    [TestMethod]
    public void PayloadFromUnselectedOriginUsesOnlyOrigin()
    {
        var payload = DragSelection.PayloadForDragStart(["one", "two"], "three", originAlreadySelected: false);

        CollectionAssert.AreEqual(new[] { "three" }, payload.ToArray());
    }
}
