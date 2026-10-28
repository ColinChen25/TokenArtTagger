using System.IO;
using System.Text.Json;
using TokenArtTagger.Automation;
using TokenArtTagger.Core;

namespace TokenArtTagger.App.ViewModels;

public sealed partial class MainViewModel
{
    private string _scanGeneration = Guid.NewGuid().ToString("N");
    private string? _scannedRoot;
    private bool _scanRecursive = true;
    private string[] _scanExclusions = [];
    private IReadOnlyList<FileOperationError> _scanErrors = [];
    private int _workspaceOperations;
    private bool _agentBusy;
    private CancellationToken _agentOperationToken;
    private bool _agentAccessEnabled = true;
    private string _agentStatus = "Agent access ready";
    public bool AgentAccessEnabled { get => _agentAccessEnabled; set => SetProperty(ref _agentAccessEnabled, value); }
    public string AgentStatus { get => _agentStatus; private set => SetProperty(ref _agentStatus, value); }
    public bool IsWorkspaceAvailable => !_agentBusy && _workspaceOperations == 0;
    public event Action<IReadOnlyList<ImageItemViewModel>>? RequestAgentSelection;

    private async Task RunWorkspaceOperationAsync(Func<Task> operation)
    {
        _workspaceOperations++;
        OnPropertyChanged(nameof(IsWorkspaceAvailable));
        try { await operation(); }
        finally { _workspaceOperations--; OnPropertyChanged(nameof(IsWorkspaceAvailable)); }
    }
    public Task FlushTemporaryTagsAsync() => RunWorkspaceOperationAsync(() => SaveWorkInProgressAsync());
    public void AgentConnectionFailed() => AgentStatus = "Agent connection unavailable; restart app";
    private string ImageId(ImageItemViewModel image) => AgentJson.Hash(_scanGeneration + image.FullPath.ToLowerInvariant());
    private string WorkspaceRevision => AgentJson.Hash(_scanGeneration + JsonSerializer.Serialize(Items.Select(i => new { i.FullPath, i.Item.CurrentTags }), AgentJson.Options));
    private string ImageRevision(ImageItemViewModel image) => AgentJson.Hash(ImageId(image) + FileIdentity.FromPath(image.FullPath).Key + JsonSerializer.Serialize(image.Item.CurrentTags, AgentJson.Options));

    private object Describe(ImageItemViewModel image)
    {
        string? revision = null;
        string? error = null;
        try { revision = ImageRevision(image); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { error = ex.Message; }
        return new
        {
            id = ImageId(image), revision, path = image.FullPath, filename = image.FileName,
            filenameTags = image.Item.ParsedTags, currentTags = image.Item.CurrentTags,
            recognizedFilename = image.Item.IsRecognized, changed = image.IsDirty,
            tagIssue = image.TagIssue.Kind.ToString(), tagIssueMessage = image.TagIssue.Message,
            proposedFilename = image.ProposedFileName, previewError = image.PreviewError, fileError = error
        };
    }
    private object DescribeStatus() => new
    {
        appVersion = AppInfo.Version, processId = Environment.ProcessId, workspaceRevision = WorkspaceRevision,
        root = _scannedRoot, recursive = _scanRecursive, excludeDirectories = _scanExclusions,
        count = Items.Count, tagged = Items.Count(i => i.Item.IsRecognized), untagged = Items.Count(i => !i.Item.IsRecognized),
        changed = Items.Count(i => i.IsDirty), incomplete = Items.Count(i => i.HasIncompleteTags), invalid = Items.Count(i => i.HasInvalidTags),
        selectedIds = SelectedItems.Select(ImageId).ToArray(), scanErrors = _scanErrors,
        genders = Counts(i => i.Item.CurrentTags.Gender), roles = Counts(i => i.Item.CurrentTags.Role),
        races = Counts(i => i.Item.CurrentTags.Race), styles = Counts(i => i.Item.CurrentTags.WeaponOrStyle),
        statusMessage = StatusMessage, renameConfirmation = "desktop-only"
    };
    private Dictionary<string, int> Counts(Func<ImageItemViewModel, string?> field) => Items.GroupBy(i => field(i) ?? "unresolved").OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.Count());
    private void CheckWorkspaceRevision(JsonElement args)
    {
        if (args.OptionalString("expectedWorkspaceRevision") is { } revision && revision != WorkspaceRevision)
            throw new AgentException("stale_workspace", "Workspace changed. Read app_status or restart pagination.");
    }
    private ImageItemViewModel Resolve(string id) => Items.FirstOrDefault(i => ImageId(i) == id)
        ?? throw new AgentException("unknown_image", "Image ID not in this scan. Call list_images again.");
    private ImageItemViewModel ResolveTarget(JsonElement target)
    {
        var item = Resolve(target.RequiredString("id"));
        if (target.RequiredString("expectedRevision") != ImageRevision(item))
            throw new AgentException("stale_image", "Image content metadata or tags changed. Read current details before editing.");
        return item;
    }
    private ImageItemViewModel[] ResolveTargets(JsonElement args, string property)
    {
        var targets = args.GetProperty(property).EnumerateArray().Select(ResolveTarget).ToArray();
        if (targets.Distinct().Count() != targets.Length) throw new AgentException("duplicate_target", "Each image may appear only once in a batch.");
        return targets;
    }
    private static bool Within(string path, string root) => string.Equals(path.TrimEnd(Path.DirectorySeparatorChar), root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    // Always entered on the WPF dispatcher. UI controls are disabled for the duration of each request.
    public async Task<AgentResult> HandleAgentRequestAsync(AgentRequest request, CancellationToken token)
    {
        using var requestLog = DebugLog?.Enter("AgentRequest", details: new Dictionary<string, string?> { ["tool"] = request.Name });
        if (!AgentAccessEnabled) return AgentResult.Error("agent_disabled", "Enable Agent access in the desktop app.");
        if (!IsWorkspaceAvailable) return AgentResult.Error("app_busy", "The desktop is scanning, previewing, or applying another operation. Retry after it completes.");
        AgentToolCatalog.ValidateArguments(request.Name, request.Arguments);
        _agentBusy = true;
        _agentOperationToken = token;
        OnPropertyChanged(nameof(IsWorkspaceAvailable));
        AgentStatus = $"Agent: {request.Name}";
        try
        {
            token.ThrowIfCancellationRequested();
            var args = request.Arguments;
            switch (request.Name)
            {
                case "app_status": return AgentResult.Data(DescribeStatus());
                case "tag_schema": return AgentResult.Data(AgentToolCatalog.Schema);
                case "queue_ai_suggestions": return await QueueAiSuggestionsAsync(args, token);
                case "list_ai_suggestions": return await ListAiSuggestionsAsync(args, token);
                case "scan_images":
                    CheckWorkspaceRevision(args);
                    var root = args.RequiredString("root");
                    if (!Path.IsPathFullyQualified(root) || !Directory.Exists(root)) throw new AgentException("invalid_root", "root must be an existing absolute folder.");
                    root = Path.GetFullPath(root);
                    var excluded = args.TryGetProperty("excludeDirectories", out var excludes) ? excludes.EnumerateArray().Select(e => e.GetString()!).ToArray() : [];
                    if (excluded.Any(e => !Path.IsPathFullyQualified(e) || !Within(Path.GetFullPath(e), root)))
                        throw new AgentException("invalid_exclusion", "Excluded directories must be absolute paths within root.");
                    _scanRecursive = args.Boolean("recursive", true);
                    _scanExclusions = excluded.Select(Path.GetFullPath).ToArray();
                    FolderPath = root;
                    await ScanAsync();
                    return AgentResult.Data(DescribeStatus());
                case "list_images":
                    CheckWorkspaceRevision(args);
                    IEnumerable<ImageItemViewModel> matches = Items;
                    if (args.OptionalString("folder") is { } folder)
                    {
                        if (!Path.IsPathFullyQualified(folder) || _scannedRoot is null || !Within(Path.GetFullPath(folder), _scannedRoot))
                            throw new AgentException("invalid_folder", "folder must be inside the scanned root.");
                        folder = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar);
                        matches = matches.Where(i => args.Boolean("recursive", true) ? Within(i.Item.DirectoryPath, folder) : string.Equals(i.Item.DirectoryPath.TrimEnd(Path.DirectorySeparatorChar), folder, StringComparison.OrdinalIgnoreCase));
                    }
                    if (args.OptionalString("query") is { } query) matches = matches.Where(i => ImageSearchMatcher.Matches(i.Item, query));
                    matches = args.OptionalString("state") switch
                    {
                        "tagged" => matches.Where(i => i.Item.IsRecognized), "untagged" => matches.Where(i => !i.Item.IsRecognized),
                        "changed" => matches.Where(i => i.IsDirty), "incomplete" => matches.Where(i => i.HasIncompleteTags), "invalid" => matches.Where(i => i.HasInvalidTags), _ => matches
                    };
                    var filtered = matches.OrderBy(i => i.FullPath, StringComparer.OrdinalIgnoreCase).ToArray();
                    var offset = args.Integer("offset", 0);
                    var page = filtered.Skip(offset).Take(args.Integer("limit", 50)).ToArray();
                    return AgentResult.Data(new { workspaceRevision = WorkspaceRevision, total = filtered.Length, offset, nextOffset = (long)offset + page.Length < filtered.Length ? (int?) (offset + page.Length) : null, images = page.Select(Describe).ToArray() });
                case "image_details":
                    var detail = Resolve(args.RequiredString("id"));
                    var beforeHash = ImageRevision(detail);
                    var hash = args.Boolean("includeHash") ? await ContentHasher.HashFileHexAsync(detail.FullPath, token) : null;
                    if (beforeHash != ImageRevision(detail)) throw new AgentException("stale_image", "File changed while hashing.");
                    return AgentResult.Data(new { image = Describe(detail), sha256 = hash });
                case "view_image":
                    var image = ResolveTarget(args);
                    var pixels = await Services.AgentImageService.ReadAsync(image.FullPath, args.Integer("maxDimension", 1024), token);
                    ResolveTarget(args);
                    var data = new { image = Describe(image), pixels.Width, pixels.Height, frame = "first", pixelsMayBeSentToCallingModel = true };
                    return new AgentResult([new { type = "text", text = JsonSerializer.Serialize(data, AgentJson.Options) }, new { type = "image", data = pixels.Base64, mimeType = "image/png" }], data);
                case "update_tags":
                case "reset_tags":
                    var property = request.Name == "update_tags" ? "changes" : "targets";
                    var targets = ResolveTargets(args, property);
                    var replacements = request.Name == "update_tags"
                        ? args.GetProperty(property).EnumerateArray().Select(e => AgentTagRules.ReadReplacement(e.GetProperty("tags"))).ToArray()
                        : targets.Select(i => i.Item.ParsedTags).ToArray();
                    var previous = targets.Select(i => i.Item).ToArray();
                    for (var i = 0; i < targets.Length; i++) targets[i].ReplaceItem(previous[i] with { CurrentTags = replacements[i] });
                    UpdateAfterTagsChanged(save: false);
                    try { await SaveWorkInProgressAsync(); }
                    catch
                    {
                        for (var i = 0; i < targets.Length; i++) targets[i].ReplaceItem(previous[i]);
                        UpdateAfterTagsChanged(save: false);
                        throw;
                    }
                    SetStatus($"Agent saved temporary tags for {targets.Length} image(s).");
                    return AgentResult.Data(new { workspaceRevision = WorkspaceRevision, persisted = true, images = targets.Select(Describe).ToArray() });
                case "select_images":
                case "preview_rename":
                    var selected = ResolveTargets(args, "targets");
                    FilterText = string.Empty;
                    _filterRefreshCancellation?.Cancel();
                    _appliedFilterText = string.Empty;
                    ItemsView.Refresh();
                    RequestAgentSelection?.Invoke(selected);
                    ReplaceSelection(selected);
                    if (request.Name == "preview_rename") await PreviewRenameAsync(RenamePreviewScope.Selected);
                    return AgentResult.Data(new { workspaceRevision = WorkspaceRevision, images = selected.Select(Describe).ToArray(), statusMessage = StatusMessage, canRename = _lastPreview?.CanRename ?? false, confirmation = "Use Rename Selected in the desktop app to confirm; no files were renamed." });
                default: return AgentResult.Error("unknown_tool", request.Name);
            }
        }
        catch (AgentException ex) { return AgentResult.Error(ex.Code, ex.Message); }
        finally
        {
            _agentBusy = false;
            _agentOperationToken = default;
            OnPropertyChanged(nameof(IsWorkspaceAvailable));
            AgentStatus = $"Agent last used: {request.Name}";
        }
    }
}
