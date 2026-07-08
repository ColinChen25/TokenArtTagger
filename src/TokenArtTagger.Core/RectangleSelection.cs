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
    public static SelectionRectangle Invalid => new(double.NaN, double.NaN, double.NaN, double.NaN);

    public double Left => Math.Min(X, X + Width);
    public double Top => Math.Min(Y, Y + Height);
    public double Right => Math.Max(X, X + Width);
    public double Bottom => Math.Max(Y, Y + Height);

    public bool IsValid => IsFinite(X) && IsFinite(Y) && IsFinite(Width) && IsFinite(Height);

    public SelectionRectangle ClampTo(SelectionRectangle bounds)
    {
        if (!IsValid || !bounds.IsValid)
        {
            return Invalid;
        }

        var left = Math.Max(Left, bounds.Left);
        var top = Math.Max(Top, bounds.Top);
        var right = Math.Min(Right, bounds.Right);
        var bottom = Math.Min(Bottom, bounds.Bottom);

        return left <= right && top <= bottom
            ? new SelectionRectangle(left, top, right - left, bottom - top)
            : Invalid;
    }

    public bool Intersects(SelectionRectangle other)
    {
        return IsValid &&
            other.IsValid &&
            Left <= other.Right &&
            Right >= other.Left &&
            Top <= other.Bottom &&
            Bottom >= other.Top;
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
