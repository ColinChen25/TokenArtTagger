using System.IO;
using System.Text.Json;
using TokenArtTagger.Automation;
using TokenArtTagger.Core;

namespace TokenArtTagger.App.ViewModels;

public sealed partial class MainViewModel
{
    private readonly AiSuggestionStore _aiSuggestionStore = AiSuggestionStore.CreateDefault();

    private async Task<AgentResult> QueueAiSuggestionsAsync(JsonElement args, CancellationToken token)
    {
        var targets = ResolveTargets(args, "suggestions");
        var requests = args.GetProperty("suggestions").EnumerateArray().ToArray();
        var proposals = new List<AiSuggestion>();
        try
        {
            for (var i = 0; i < targets.Length; i++)
            {
                var item = targets[i];
                var request = requests[i];
                var sha = request.RequiredString("sha256");
                var provider = request.RequiredString("provider");
                var model = request.RequiredString("model");
                var run = request.RequiredString("runFingerprint");
                var row = new AiSuggestion(AiSuggestion.Identity(item.FullPath, sha, provider, model, run),
                    item.FullPath, sha, AgentTagRules.ReadReplacement(request.GetProperty("tags")), provider, model, run,
                    request.TryGetProperty("evidence", out var evidence) ? evidence.GetString()! : "", FileIdentity.FromPath(item.FullPath).Key,
                    item.Item.CurrentTags, DateTimeOffset.UtcNow);
                row.Validate();
                proposals.Add(row);
            }
            // Validate all proposals before any persistent mutation. Hash only bounded explicit targets.
            for (var i = 0; i < targets.Length; i++)
            {
                if (await ContentHasher.HashFileHexAsync(targets[i].FullPath, token) != proposals[i].Sha256)
                    throw new AgentException("source_changed", "Suggestion source hash differs from the current image. Reinspect the image and prediction source.");
            }
            ResolveTargets(args, "suggestions");
            var added = await _aiSuggestionStore.AddAsync(proposals, token);
            return AgentResult.Data(new
            {
                imported = added, alreadyPresent = proposals.Count - added, persisted = true,
                reviewRequired = true, automaticAcceptance = false, workspaceRevision = WorkspaceRevision,
                suggestionIds = proposals.Select(p => p.Id).ToArray()
            });
        }
        catch (InvalidDataException ex) { throw new AgentException("invalid_suggestion", ex.Message); }
    }

    private async Task<AgentResult> ListAiSuggestionsAsync(JsonElement args, CancellationToken token)
    {
        CheckWorkspaceRevision(args);
        var saved = await _aiSuggestionStore.LoadAsync(token);
        var queueRevision = AgentJson.Hash(JsonSerializer.Serialize(saved, AgentJson.Options));
        if (args.OptionalString("expectedQueueRevision") is { } expected && expected != queueRevision)
            throw new AgentException("stale_queue", "Saved suggestions changed; restart queue pagination.");
        var scanned = Items.ToDictionary(i => i.FullPath, StringComparer.OrdinalIgnoreCase);
        var filter = args.OptionalString("imageId") is { } id ? Resolve(id).FullPath : null;
        var matches = saved.Where(s => scanned.ContainsKey(s.ImagePath) && (filter is null || string.Equals(filter, s.ImagePath, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(s => s.ImportedUtc).ThenBy(s => s.Id, StringComparer.Ordinal).ToArray();
        var offset = args.Integer("offset", 0);
        var page = matches.Skip(offset).Take(args.Integer("limit", 50)).Select(s =>
        {
            var item = scanned[s.ImagePath];
            var metadataUnchanged = false;
            try { metadataUnchanged = FileIdentity.FromPath(item.FullPath).Key == s.FileIdentityAtImport; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            return new { suggestion = s, image = Describe(item), metadataUnchanged, tagsUnchanged = s.TagsAtImport == item.Item.CurrentTags };
        }).ToArray();
        return AgentResult.Data(new
        {
            queueRevision, workspaceRevision = WorkspaceRevision, total = matches.Length, offset,
            nextOffset = (long)offset + page.Length < matches.Length ? (int?)(offset + page.Length) : null,
            sourceContentRechecked = false, reviewRequired = true, automaticAcceptance = false, suggestions = page
        });
    }
}
