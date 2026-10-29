using System.IO;
using TokenArtTagger.App.Services;
using TokenArtTagger.Core;

namespace TokenArtTagger.App.ViewModels;

public sealed record AiReviewTarget(AiSuggestion Suggestion, TagSet CurrentTags, TagSet FilenameTags,
    string Revision, AiReviewReceipt? Receipt)
{
    public string FileName => Path.GetFileName(Suggestion.ImagePath);
    public string State => Receipt?.Outcome ?? "pending";
    public string Label => $"{FileName} · {State}";
}

public sealed partial class MainViewModel
{
    private readonly AiReviewStore _aiReviewStore = AiReviewStore.CreateDefault();
    private bool _aiReviewOpen;

    // Hold the existing workspace gate for the entire modal review, including time spent inspecting pixels.
    public async Task WithAiReviewAsync(Func<Task> review)
    {
        if (!IsWorkspaceAvailable) throw new InvalidOperationException("Wait for the current workspace operation.");
        await RunWorkspaceOperationAsync(async () =>
        {
            _aiReviewOpen = true;
            try { await review(); }
            finally { _aiReviewOpen = false; }
        });
    }

    private void RequireAiReview()
    {
        if (!_aiReviewOpen) throw new InvalidOperationException("Open the desktop AI review window first.");
    }

    public async Task<IReadOnlyList<AiReviewTarget>> LoadAiReviewsAsync()
    {
        RequireAiReview();
        var scanned = Items.ToDictionary(i => i.FullPath, StringComparer.OrdinalIgnoreCase);
        var result = new List<AiReviewTarget>();
        foreach (var proposal in (await _aiSuggestionStore.LoadAsync()).OrderBy(s => s.ImportedUtc).ThenBy(s => s.Id, StringComparer.Ordinal))
        {
            if (!scanned.TryGetValue(proposal.ImagePath, out var item)) continue;
            string revision;
            try { revision = ImageRevision(item); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { revision = "unavailable"; }
            result.Add(new(proposal, item.Item.CurrentTags, item.Item.ParsedTags, revision, await _aiReviewStore.LoadAsync(proposal.Id)));
        }
        return result;
    }

    private ImageItemViewModel ResolveReview(AiReviewTarget target)
    {
        RequireAiReview();
        var item = Items.SingleOrDefault(i => string.Equals(i.FullPath, target.Suggestion.ImagePath, StringComparison.OrdinalIgnoreCase));
        if (item is null || ImageRevision(item) != target.Revision || item.Item.CurrentTags != target.CurrentTags || item.Item.ParsedTags != target.FilenameTags)
            throw new InvalidDataException("The image or tags changed. Refresh the queue and review the current values.");
        return item;
    }

    private async Task CheckReviewSourceAsync(AiReviewTarget target)
    {
        ResolveReview(target);
        if (await ContentHasher.HashFileHexAsync(target.Suggestion.ImagePath) != target.Suggestion.Sha256)
            throw new InvalidDataException("The image contents differ from the AI source. This proposal cannot be applied; dismiss it and generate a new suggestion.");
        ResolveReview(target);
    }

    public async Task<AgentImage> LoadAiReviewPreviewAsync(AiReviewTarget target)
    {
        RequireAiReview();
        using var held = new FileStream(target.Suggestion.ImagePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        await CheckReviewSourceAsync(target);
        return await AgentImageService.ReadAsync(target.Suggestion.ImagePath, 1024, CancellationToken.None);
    }

    public async Task SaveAiReviewAsync(AiReviewTarget target, AiReviewPlan plan, bool replaceConfirmed, bool dismiss = false)
    {
        RequireAiReview();
        if (target.Receipt is not null) throw new InvalidDataException("This proposal has already been reviewed.");
        // A target must still be the exact stored proposal, including its original import snapshot.
        if (!(await _aiSuggestionStore.LoadAsync()).Any(s => s == target.Suggestion))
            throw new InvalidDataException("The proposal changed. Refresh the queue.");
        if (dismiss)
        {
            await _aiReviewStore.CommitAsync(new(1, target.Suggestion, "dismissed", target.CurrentTags,
                target.FilenameTags, AiReviewPlan.KeepAll, target.CurrentTags, false, DateTimeOffset.UtcNow));
            return;
        }
        var after = plan.Resolve(target.CurrentTags, target.Suggestion.Tags);
        var receipt = new AiReviewReceipt(1, target.Suggestion, "staged", target.CurrentTags,
            target.FilenameTags, plan, after, replaceConfirmed, DateTimeOffset.UtcNow);
        receipt.Validate();
        using var held = new FileStream(target.Suggestion.ImagePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        await CheckReviewSourceAsync(target);
        var item = ResolveReview(target);
        await _aiReviewStore.CommitAsync(receipt, async () =>
        {
            ResolveReview(target);
            var previous = item.Item;
            item.ReplaceItem(previous with { CurrentTags = after });
            UpdateAfterTagsChanged(save: false);
            try { await SaveWorkInProgressAsync(); }
            catch
            {
                item.ReplaceItem(previous);
                UpdateAfterTagsChanged(save: false);
                throw;
            }
        });
        SetStatus("Reviewed AI tags saved as temporary tags. Use Preview Changed to inspect filenames before renaming.");
    }
}
