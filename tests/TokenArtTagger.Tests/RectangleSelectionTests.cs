using TokenArtTagger.Core;

namespace TokenArtTagger.Tests;

[TestClass]
public sealed class RectangleSelectionTests
{
    [TestMethod]
    public void Intersecting_ReturnsTilesTouchedByDragRectangle()
    {
        var tiles = new[]
        {
            new SelectionTile<string>("first", new SelectionRectangle(0, 0, 50, 50)),
            new SelectionTile<string>("second", new SelectionRectangle(60, 0, 50, 50)),
            new SelectionTile<string>("third", new SelectionRectangle(140, 0, 50, 50))
        };

        var selected = RectangleSelection.Intersecting(tiles, new SelectionRectangle(45, 10, 70, 25));

        CollectionAssert.AreEqual(new[] { "first", "second" }, selected.ToArray());
    }

    [TestMethod]
    public void Intersecting_SkipsInvalidTileBounds()
    {
        var tiles = new[]
        {
            new SelectionTile<string>("bad", new SelectionRectangle(double.NaN, 0, 50, 50)),
            new SelectionTile<string>("good", new SelectionRectangle(0, 0, 50, 50))
        };

        var selected = RectangleSelection.Intersecting(tiles, new SelectionRectangle(0, 0, 20, 20));

        CollectionAssert.AreEqual(new[] { "good" }, selected.ToArray());
    }
}
