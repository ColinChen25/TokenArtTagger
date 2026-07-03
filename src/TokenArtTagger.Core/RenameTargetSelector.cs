namespace TokenArtTagger.Core;

public static class RenameTargetSelector
{
    public static IReadOnlyList<ImageItem> Selected(
        IReadOnlyCollection<ImageItem> selectedItems,
        IReadOnlyCollection<ImageItem> allItems)
    {
        return selectedItems.ToList();
    }

    public static IReadOnlyList<ImageItem> Dirty(IReadOnlyCollection<ImageItem> allItems)
    {
        return allItems.Where(item => item.IsDirty).ToList();
    }
}
