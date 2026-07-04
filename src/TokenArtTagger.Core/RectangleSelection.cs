namespace TokenArtTagger.Core;

public static class RectangleSelection
{
    public static IReadOnlyList<T> Intersecting<T>(
        IEnumerable<SelectionTile<T>> tiles,
        SelectionRectangle rectangle)
    {
        return tiles
            .Where(tile => tile.Bounds.IsValid && tile.Bounds.Intersects(rectangle))
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

    public bool IsValid => IsFinite(X) && IsFinite(Y) && IsFinite(Width) && IsFinite(Height);

    public bool Intersects(SelectionRectangle other)
    {
        return IsValid &&
            other.IsValid &&
            Left <= other.Right &&
            Right >= other.Left &&
            Top <= other.Bottom &&
            Bottom >= other.Top;
    }

    public SelectionRectangle ClampTo(SelectionRectangle bounds)
    {
        if (!IsValid || !bounds.IsValid)
        {
            return new SelectionRectangle(double.NaN, double.NaN, double.NaN, double.NaN);
        }

        var left = Math.Clamp(Left, bounds.Left, bounds.Right);
        var right = Math.Clamp(Right, bounds.Left, bounds.Right);
        var top = Math.Clamp(Top, bounds.Top, bounds.Bottom);
        var bottom = Math.Clamp(Bottom, bounds.Top, bounds.Bottom);
        return new SelectionRectangle(left, top, right - left, bottom - top);
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
