namespace TokenArtTagger.Core;

public sealed class RenamePreviewSession
{
    public RenamePreviewScope? Scope { get; private set; }

    public bool CanConfirmRename { get; private set; }

    public IReadOnlyList<ImageItem> TargetsForRenameSelectedWithoutPreview(
        IReadOnlyCollection<ImageItem> selectedItems,
        IReadOnlyCollection<ImageItem> allItems)
    {
        Scope = RenamePreviewScope.Selected;
        CanConfirmRename = false;
        return RenameTargetSelector.Selected(selectedItems, allItems);
    }

    public void MarkPreviewReady(RenamePreviewScope scope, bool canRename)
    {
        Scope = scope;
        CanConfirmRename = canRename;
    }
}

public enum RenamePreviewScope
{
    Selected,
    Changed
}
