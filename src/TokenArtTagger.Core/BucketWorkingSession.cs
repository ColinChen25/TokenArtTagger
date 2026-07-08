namespace TokenArtTagger.Core;

public sealed class BucketWorkingSession
{
    private readonly IReadOnlyList<string> _fullPaths;

    private BucketWorkingSession(BucketPass pass, BucketFilterMode filterMode, IReadOnlyList<string> fullPaths)
    {
        Pass = pass;
        FilterMode = filterMode;
        _fullPaths = fullPaths;
    }

    public BucketPass Pass { get; }

    public BucketFilterMode FilterMode { get; }

    public int Count => _fullPaths.Count;

    public static BucketWorkingSession Create(
        IReadOnlyCollection<ImageItem> items,
        BucketPass pass,
        BucketFilterMode filterMode)
    {
        var fullPaths = BucketWorkingSet.Filter(items, pass, filterMode)
            .Select(item => item.FullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new BucketWorkingSession(pass, filterMode, fullPaths);
    }

    public bool Contains(ImageItem item)
    {
        return _fullPaths.Contains(item.FullPath, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<ImageItem> Materialize(IReadOnlyCollection<ImageItem> currentItems, string searchText)
    {
        var byPath = currentItems.ToDictionary(item => item.FullPath, StringComparer.OrdinalIgnoreCase);
        return _fullPaths
            .Select(path => byPath.TryGetValue(path, out var item) ? item : null)
            .OfType<ImageItem>()
            .Where(item => ImageSearchMatcher.Matches(item, searchText))
            .ToList();
    }

    public int CompletedThisPassCount(IReadOnlyCollection<ImageItem> currentItems)
    {
        return Materialize(currentItems, string.Empty)
            .Count(item => BucketWorkingSet.IsCompleteForPass(item, Pass));
    }

    public int ReadyToRenameCount(IReadOnlyCollection<ImageItem> currentItems)
    {
        return Materialize(currentItems, string.Empty)
            .Count(item => RenameReadiness.Evaluate([item]).CanPreview);
    }
}
