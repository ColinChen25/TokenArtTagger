using System.Text.Json;

namespace TokenArtTagger.Core;

public sealed class WorkInProgressTagStore
{
    private readonly string _storePath;

    public WorkInProgressTagStore(string storePath)
    {
        _storePath = storePath;
    }

    public static WorkInProgressTagStore CreateDefault()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TokenArtTagger");
        return new WorkInProgressTagStore(Path.Combine(folder, "work-in-progress-tags.json"));
    }

    public async Task<IReadOnlyList<WorkInProgressTagRecord>> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_storePath))
        {
            return [];
        }

        await using var stream = File.OpenRead(_storePath);
        var records = await JsonSerializer.DeserializeAsync<List<WorkInProgressTagRecord>>(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return records ?? [];
    }

    public async Task SaveAsync(
        IReadOnlyList<WorkInProgressTagRecord> records,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_storePath)!);
        var options = new JsonSerializerOptions { WriteIndented = true };
        await using var stream = File.Create(_storePath);
        await JsonSerializer.SerializeAsync(stream, records, options, cancellationToken).ConfigureAwait(false);
    }
}

public sealed record WorkInProgressTagRecord(string IdentityKey, TagSet Tags);
