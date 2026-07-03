namespace TokenArtTagger.Core;

// Future SQLite/freeform tag support can replace this in-memory repository without changing callers.
public interface IImageRepository
{
    IReadOnlyList<ImageItem> Items { get; }

    void ReplaceAll(IEnumerable<ImageItem> items);
}

public sealed class InMemoryImageRepository : IImageRepository
{
    private readonly List<ImageItem> _items = [];

    public IReadOnlyList<ImageItem> Items => _items;

    public void ReplaceAll(IEnumerable<ImageItem> items)
    {
        _items.Clear();
        _items.AddRange(items);
    }
}
