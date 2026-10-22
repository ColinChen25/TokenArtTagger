using System.Text.Json;
using System.Collections.Concurrent;

namespace TokenArtTagger.Core;

public sealed class WorkInProgressTagStore
{
    private readonly string _storePath;
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.OrdinalIgnoreCase);
    private SemaphoreSlim Gate => Gates.GetOrAdd(_storePath, _ => new SemaphoreSlim(1, 1));

    public WorkInProgressTagStore(string storePath)
    {
        _storePath = Path.GetFullPath(storePath);
    }

    public static WorkInProgressTagStore CreateDefault()
    {
        var folder = AppDataPaths.Root;
        return new WorkInProgressTagStore(Path.Combine(folder, "work-in-progress-tags.json"));
    }

    public async Task<IReadOnlyList<WorkInProgressTagRecord>> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_storePath))
        {
            return [];
        }

        await using var stream = new FileStream(_storePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        var records = await JsonSerializer.DeserializeAsync<List<WorkInProgressTagRecord>>(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return records ?? [];
    }

    public async Task SaveAsync(
        IReadOnlyList<WorkInProgressTagRecord> records,
        CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try { await WriteAsync(records, cancellationToken); }
        finally { Gate.Release(); }
    }

    public async Task MergeAsync(IReadOnlyList<WorkInProgressTagRecord> records, IReadOnlyCollection<string> scopedPaths, CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            var scope = scopedPaths.Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var existing = await LoadAsync(cancellationToken);
            var retained = existing.Where(r => !scope.Contains(r.IdentityKey.Split('|')[0]));
            await WriteAsync(retained.Concat(records).GroupBy(r => r.IdentityKey, StringComparer.OrdinalIgnoreCase).Select(g => g.Last()).ToArray(), cancellationToken);
        }
        finally { Gate.Release(); }
    }

    private async Task WriteAsync(IReadOnlyList<WorkInProgressTagRecord> records, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_storePath)!);
        var options = new JsonSerializerOptions { WriteIndented = true };
        var temporary = _storePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, records, options, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, _storePath, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try { File.Delete(_storePath); }
        finally { Gate.Release(); }
    }
}

public sealed record WorkInProgressTagRecord(string IdentityKey, TagSet Tags);
