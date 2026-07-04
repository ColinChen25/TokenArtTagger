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

    [TestMethod]
    public void ClampTo_KeepsDragRectangleInsideGridBounds()
    {
        var rectangle = new SelectionRectangle(20, -25, 60, 80);
        var bounds = new SelectionRectangle(0, 0, 100, 100);

        var clamped = rectangle.ClampTo(bounds);

        Assert.AreEqual(20, clamped.Left);
        Assert.AreEqual(0, clamped.Top);
        Assert.AreEqual(80, clamped.Right);
        Assert.AreEqual(55, clamped.Bottom);
    }

    [TestMethod]
    public void Intersecting_ReturnsEmptyForInvalidSelectionRectangle()
    {
        var tiles = new[]
        {
            new SelectionTile<string>("first", new SelectionRectangle(0, 0, 50, 50))
        };

        var selected = RectangleSelection.Intersecting(tiles, new SelectionRectangle(double.NaN, 0, 20, 20));

        Assert.AreEqual(0, selected.Count);
    }
}
