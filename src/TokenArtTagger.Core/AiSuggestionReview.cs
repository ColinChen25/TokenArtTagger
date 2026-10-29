using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TokenArtTagger.Core;

public enum AiFieldAction { Keep, Suggestion, Set, Clear }
public sealed record AiFieldDecision(AiFieldAction Action = AiFieldAction.Keep, string? Value = null)
{
    public string? Resolve(string? current, string? proposed) => Action switch
    {
        AiFieldAction.Keep when Value is null => current,
        AiFieldAction.Suggestion when Value is null => proposed,
        AiFieldAction.Clear when Value is null => null,
        AiFieldAction.Set when !string.IsNullOrWhiteSpace(Value) => Value,
        _ => throw new InvalidDataException("Invalid field decision.")
    };
}

public sealed record AiReviewPlan(AiFieldDecision Gender, AiFieldDecision Role, AiFieldDecision Style, AiFieldDecision Race)
{
    public static AiReviewPlan KeepAll => new(new(), new(), new(), new());
    public bool HasDecision => new[] { Gender, Role, Style, Race }.Any(d => d.Action != AiFieldAction.Keep);
    public TagSet Resolve(TagSet current, TagSet proposed)
    {
        if (Gender is null || Role is null || Style is null || Race is null)
            throw new InvalidDataException("Choose an action for every field.");
        var result = new TagSet(Gender.Resolve(current.Gender, proposed.Gender), Role.Resolve(current.Role, proposed.Role),
            Style.Resolve(current.WeaponOrStyle, proposed.WeaponOrStyle), Race.Resolve(current.Race, proposed.Race));
        AiSuggestion.ValidateTags(result);
        return result;
    }

    public static bool ReplacesExisting(TagSet before, TagSet filename, TagSet after)
    {
        static bool Changed(string? existing, string? replacement) => existing is not null && existing != replacement;
        return new[] { before, filename }.Any(t => Changed(t.Gender, after.Gender) || Changed(t.Role, after.Role) ||
            Changed(t.WeaponOrStyle, after.WeaponOrStyle) || Changed(t.Race, after.Race));
    }
}

// An immutable receipt records an explicit desktop decision, never a training label.
public sealed record AiReviewReceipt(int Version, AiSuggestion Suggestion, string Outcome, TagSet Before,
    TagSet FilenameTags, AiReviewPlan Decisions, TagSet After, bool ReplaceExistingConfirmed, DateTimeOffset ReviewedUtc)
{
    public void Validate()
    {
        if (Version != 1 || Suggestion is null || Before is null || FilenameTags is null || Decisions is null || After is null ||
            ReviewedUtc < DateTimeOffset.UnixEpoch || Outcome is not ("staged" or "dismissed"))
            throw new InvalidDataException("Invalid review receipt.");
        Suggestion.Validate();
        if (Outcome == "dismissed")
        {
            if (Before != After || Decisions != AiReviewPlan.KeepAll)
                throw new InvalidDataException("Dismissal cannot change tags.");
        }
        else
        {
            if (Decisions.Resolve(Before, Suggestion.Tags) != After || !Decisions.HasDecision)
                throw new InvalidDataException("Review result differs from field decisions.");
            if (AiReviewPlan.ReplacesExisting(Before, FilenameTags, After) && !ReplaceExistingConfirmed)
                throw new InvalidDataException("Confirm replacement of existing tags.");
        }
    }
}

public sealed class AiReviewReceiptException(Exception inner) : IOException(
    "Temporary tags were saved, but the review receipt could not be saved. The proposal remains pending; reload and inspect the current tags before retrying.", inner);

public sealed class AiReviewStore
{
    private const int MaxBytes = 256 * 1024;
    private readonly string _directory;
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    { WriteIndented = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    public AiReviewStore(string directory) => _directory = Path.GetFullPath(directory);
    public static AiReviewStore CreateDefault() => new(Path.Combine(AppDataPaths.Root, "AiSuggestions", "Reviews"));
    private string ReceiptPath(string id)
    {
        if (id.Length != 64 || id.Any(c => c is not (>= 'a' and <= 'f' or >= '0' and <= '9')))
            throw new InvalidDataException("Invalid suggestion ID.");
        return Path.Combine(_directory, id + ".json");
    }
    public async Task<AiReviewReceipt?> LoadAsync(string id, CancellationToken token = default)
    {
        var path = ReceiptPath(id);
        if (!File.Exists(path)) return null;
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length > MaxBytes) throw new InvalidDataException("Review receipt exceeds the supported size.");
        var receipt = await JsonSerializer.DeserializeAsync<AiReviewReceipt>(input, Options, token).ConfigureAwait(false)
            ?? throw new InvalidDataException("Empty review receipt.");
        receipt.Validate();
        if (receipt.Suggestion.Id != id) throw new InvalidDataException("Review receipt identity mismatch.");
        return receipt;
    }

    // Prepare the receipt before staging. A receipt is published only after successful persistence.
    // Two files cannot commit atomically: a crash after staging leaves a pending proposal, never a false acceptance.
    public async Task CommitAsync(AiReviewReceipt receipt, Func<Task>? persistTags = null, CancellationToken token = default)
    {
        receipt.Validate();
        if ((receipt.Outcome == "staged") != (persistTags is not null))
            throw new InvalidDataException("Staging requires a persistence operation; dismissal must not apply tags.");
        var path = ReceiptPath(receipt.Suggestion.Id);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(receipt, Options);
        if (bytes.Length > MaxBytes) throw new InvalidDataException("Review receipt exceeds the supported size.");
        var gate = Gates.GetOrAdd(_directory, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(token);
        try
        {
            Directory.CreateDirectory(_directory);
            await using var fileLock = new FileStream(Path.Combine(_directory, "review.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            if (File.Exists(path)) throw new InvalidDataException("This proposal has already been reviewed. Refresh the queue.");
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            var staged = false;
            try
            {
                await File.WriteAllBytesAsync(temporary, bytes, token);
                token.ThrowIfCancellationRequested();
                if (persistTags is not null) { await persistTags(); staged = true; }
                // Do not cancel between the successful tag save and its receipt.
                File.Move(temporary, path);
            }
            catch (Exception ex) when (staged) { throw new AiReviewReceiptException(ex); }
            finally
            {
                // Cleanup failure must not hide whether tag persistence already succeeded.
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        finally { gate.Release(); }
    }
}
