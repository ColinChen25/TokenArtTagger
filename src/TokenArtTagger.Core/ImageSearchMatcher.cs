namespace TokenArtTagger.Core;

public static class ImageSearchMatcher
{
    public static bool Matches(ImageItem item, string searchText)
    {
        if (string.IsNullOrWhiteSpace(searchText))
        {
            return true;
        }

        var haystack = string.Join(
            ' ',
            item.FileName,
            item.CurrentTags.Gender,
            item.CurrentTags.Role,
            item.CurrentTags.WeaponOrStyle,
            item.CurrentTags.Race);

        return haystack.Contains(searchText.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
