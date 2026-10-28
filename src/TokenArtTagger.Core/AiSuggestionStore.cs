using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TokenArtTagger.Core;

// Proposals are deliberately stored separately from work-in-progress tags.
public sealed record AiSuggestion(
    string Id, string ImagePath, string Sha256, TagSet Tags, string Provider, string Model,
    string RunFingerprint, string Evidence, string FileIdentityAtImport, TagSet TagsAtImport,
    DateTimeOffset ImportedUtc)
{
    public static string Identity(string path, string sha256, string provider, string model, string runFingerprint) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
            new[] { Path.GetFullPath(path).ToUpperInvariant(), sha256, provider, model, runFingerprint })))).ToLowerInvariant();

    public void Validate()
    {
        static bool Hash(string? value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
        if (string.IsNullOrWhiteSpace(ImagePath) || !Path.IsPathFullyQualified(ImagePath) || ImagePath.Length > 32767 ||
            !Hash(Sha256) || !Hash(RunFingerprint) || string.IsNullOrWhiteSpace(Provider) || Provider.Length > 80 ||
            string.IsNullOrWhiteSpace(Model) || Model.Length > 256 || Evidence is null || Evidence.Length > 2000 ||
            string.IsNullOrWhiteSpace(FileIdentityAtImport) || FileIdentityAtImport.Length > 33000 ||
            Tags is null || TagsAtImport is null || ImportedUtc < DateTimeOffset.UnixEpoch)
            throw new InvalidDataException("Invalid suggestion identity or provenance.");
        if (Id != Identity(ImagePath, Sha256, Provider, Model, RunFingerprint))
            throw new InvalidDataException("Suggestion identity differs from its provenance.");
        ValidateTags(Tags);
        ValidateTags(TagsAtImport);
    }

    private static void ValidateTags(TagSet tags)
    {
        if (tags.Gender is not null && !TagSchema.Genders.Contains(tags.Gender) ||
            tags.Role is not null && !TagSchema.Roles.Contains(tags.Role) ||
            tags.Race is not null && !TagSchema.Races.Contains(tags.Race) ||
            tags.WeaponOrStyle is not null && (tags.Role is null || !TagSchema.StylesForRole(tags.Role).Contains(tags.WeaponOrStyle)))
            throw new InvalidDataException("Suggestion contains unknown or incompatible tags.");
    }

    public bool SameProposal(AiSuggestion other) => Id == other.Id && Tags == other.Tags && Evidence == other.Evidence;
}

public sealed class AiSuggestionStore
{
    private const int MaxRecords = 5000;
    private const long MaxBytes = 16 * 1024 * 1024;
    private sealed record Document(int Version, AiSuggestion[] Suggestions);
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _path;

    public AiSuggestionStore(string path) => _path = Path.GetFullPath(path);
    public static AiSuggestionStore CreateDefault() => new(Path.Combine(AppDataPaths.Root, "AiSuggestions", "queue-v1.json"));

    public async Task<IReadOnlyList<AiSuggestion>> LoadAsync(CancellationToken token = default)
    {
        if (!File.Exists(_path)) return [];
        await using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        if (stream.Length > MaxBytes) throw new InvalidDataException("Suggestion queue exceeds the supported size.");
        var document = await JsonSerializer.DeserializeAsync<Document>(stream, Options, token).ConfigureAwait(false);
        if (document is null || document.Version != 1 || document.Suggestions is null || document.Suggestions.Length > MaxRecords)
            throw new InvalidDataException("Unsupported suggestion queue.");
        var ids = new HashSet<string>();
        foreach (var row in document.Suggestions)
        {
            if (row is null) throw new InvalidDataException("Suggestion is missing.");
            row.Validate();
            if (!ids.Add(row.Id)) throw new InvalidDataException("Duplicate stored suggestion.");
        }
        return document.Suggestions;
    }

    public async Task<int> AddAsync(IReadOnlyList<AiSuggestion> suggestions, CancellationToken token = default)
    {
        if (suggestions.Count is < 1 or > 100) throw new InvalidDataException("Import between one and 100 suggestions.");
        foreach (var row in suggestions) row.Validate();
        if (suggestions.Select(s => s.Id).Distinct().Count() != suggestions.Count)
            throw new InvalidDataException("Duplicate suggestion in batch.");
        var gate = Gates.GetOrAdd(_path, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(token);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            // The OS lock also excludes writers in other desktop instances.
            await using var fileLock = new FileStream(_path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            var existing = (await LoadAsync(token)).ToDictionary(s => s.Id);
            var added = 0;
            foreach (var row in suggestions)
            {
                if (existing.TryGetValue(row.Id, out var saved))
                {
                    if (!saved.SameProposal(row)) throw new InvalidDataException("Conflicting proposal for the same source run; use its original result or a new run fingerprint.");
                }
                else { existing.Add(row.Id, row); added++; }
            }
            if (existing.Count > MaxRecords) throw new InvalidDataException("Suggestion queue is full; no proposals were imported.");
            token.ThrowIfCancellationRequested();
            if (added == 0) return 0;
            var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    await JsonSerializer.SerializeAsync(stream, new Document(1, existing.Values.ToArray()), Options, token).ConfigureAwait(false);
                    if (stream.Length > MaxBytes) throw new InvalidDataException("Suggestion queue exceeds the supported size.");
                    await stream.FlushAsync(token);
                }
                token.ThrowIfCancellationRequested();
                File.Move(temporary, _path, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            return added;
        }
        finally { gate.Release(); }
    }
}
