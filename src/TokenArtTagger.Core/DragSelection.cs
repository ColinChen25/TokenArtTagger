namespace TokenArtTagger.Core;

public static class DragSelection
{
    public static IReadOnlyList<T> PayloadForDragStart<T>(
        IEnumerable<T> currentSelection,
        T origin,
        bool originAlreadySelected)
        where T : notnull
    {
        if (!originAlreadySelected)
        {
            return [origin];
        }

        var selected = currentSelection.Distinct().ToList();
        return selected.Count == 0 ? [origin] : selected;
    }
}
