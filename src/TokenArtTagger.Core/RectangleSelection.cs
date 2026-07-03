namespace TokenArtTagger.Core;

public static class RectangleSelection
{
    public static IReadOnlyList<T> Intersecting<T>(
        IEnumerable<SelectionTile<T>> tiles,
        SelectionRectangle rectangle)
    {
        return tiles
            .Where(tile => tile.Bounds.Intersects(rectangle))
            .Select(tile => tile.Value)
            .ToList();
    }
}

public sealed record SelectionTile<T>(T Value, SelectionRectangle Bounds);

public readonly record struct SelectionRectangle(double X, double Y, double Width, double Height)
{
    public double Left => Math.Min(X, X + Width);
    public double Top => Math.Min(Y, Y + Height);
    public double Right => Math.Max(X, X + Width);
    public double Bottom => Math.Max(Y, Y + Height);

    public bool Intersects(SelectionRectangle other)
    {
        return Left <= other.Right &&
            Right >= other.Left &&
            Top <= other.Bottom &&
            Bottom >= other.Top;
    }
}
