using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TokenArtTagger.Core;

namespace TokenArtTagger.App.ViewModels;

public sealed record AiFieldOption(string Label, AiFieldDecision Decision);
public sealed class AiReviewFieldViewModel : ViewModelBase
{
    public string Name { get; }
    public string Current { get; }
    public string Suggested { get; }
    public IReadOnlyList<AiFieldOption> Options { get; }
    private AiFieldOption _selected;
    public AiFieldOption Selected
    {
        get => _selected;
        set { if (value is not null && Options.Contains(value)) SetProperty(ref _selected, value); }
    }
    public AiReviewFieldViewModel(string name, string? current, string? suggested, IEnumerable<string> allowed)
    {
        Name = name;
        Current = current ?? "Unresolved";
        Suggested = suggested ?? "Unresolved";
        Options = new[] {
            new AiFieldOption($"Keep current: {Current}", new()),
            new AiFieldOption($"Use AI: {Suggested}", new(AiFieldAction.Suggestion)),
            new AiFieldOption("Leave unresolved", new(AiFieldAction.Clear))
        }.Concat(allowed.Select(v => new AiFieldOption($"Set: {v}", new(AiFieldAction.Set, v)))).ToArray();
        _selected = Options[0];
    }
}

public sealed class AiReviewViewModel : ViewModelBase
{
    private readonly MainViewModel _workspace;
    private IReadOnlyList<AiReviewTarget> _all = [];
    private AiReviewTarget? _selected;
    private bool _busy, _includeReviewed, _replaceConfirmed, _previewVerified;
    private int _selectionGeneration;
    private readonly SemaphoreSlim _previewGate = new(1, 1);
    private ImageSource? _preview;
    private string _message = "Loading saved proposals…";
    public ObservableCollection<AiReviewTarget> Proposals { get; } = [];
    public ObservableCollection<AiReviewFieldViewModel> Fields { get; } = [];
    public AiReviewViewModel(MainViewModel workspace)
    {
        _workspace = workspace;
        RefreshCommand = new AsyncRelayCommand(_ => RefreshAsync(), _ => !Busy);
        SaveCommand = new AsyncRelayCommand(_ => SaveAsync(false), _ => CanSave);
        DismissCommand = new AsyncRelayCommand(_ => SaveAsync(true), _ => !Busy && Selected is { Receipt: null });
    }
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand SaveCommand { get; }
    public AsyncRelayCommand DismissCommand { get; }
    public bool Busy { get => _busy; private set { SetProperty(ref _busy, value); OnPropertyChanged(nameof(Editable)); OnPropertyChanged(nameof(CanNavigate)); RaiseStates(); } }
    public bool CanNavigate => !Busy;
    public bool Editable => !Busy && Selected is { Receipt: null };
    public bool IncludeReviewed { get => _includeReviewed; set { if (SetProperty(ref _includeReviewed, value)) Filter(); } }
    public bool ReplaceConfirmed { get => _replaceConfirmed; set { SetProperty(ref _replaceConfirmed, value); RaiseStates(); } }
    public string Message { get => _message; private set => SetProperty(ref _message, value); }
    public ImageSource? Preview { get => _preview; private set => SetProperty(ref _preview, value); }
    public string CountText => $"{_all.Count(t => t.Receipt is null)} pending · {_all.Count(t => t.Receipt is not null)} reviewed in this scan";
    public AiReviewTarget? Selected
    {
        get => _selected;
        set
        {
            if (!SetProperty(ref _selected, value)) return;
            _selectionGeneration++;
            _previewVerified = false;
            Preview = null;
            ReplaceConfirmed = false;
            foreach (var row in Fields) row.PropertyChanged -= FieldChanged;
            Fields.Clear();
            if (value is not null)
            {
                var current = value.CurrentTags;
                var proposed = value.Suggestion.Tags;
                Fields.Add(new("Gender", current.Gender, proposed.Gender, TagSchema.Genders));
                Fields.Add(new("Role", current.Role, proposed.Role, TagSchema.Roles));
                Fields.Add(new("Weapon / style", current.WeaponOrStyle, proposed.WeaponOrStyle,
                    TagSchema.MeleeStyles.Concat(TagSchema.RangeStyles).Concat(TagSchema.CasterStyles).Distinct()));
                Fields.Add(new("Race", current.Race, proposed.Race, TagSchema.Races));
                foreach (var row in Fields) row.PropertyChanged += FieldChanged;
            }
            OnPropertyChanged(nameof(Editable));
            OnPropertyChanged(nameof(Provenance));
            OnPropertyChanged(nameof(Evidence));
            OnPropertyChanged(nameof(ReviewHistory));
            OnPropertyChanged(nameof(ImportNotice));
            RaiseStates();
            if (value is not null) _ = LoadPreviewAsync(value, _selectionGeneration);
        }
    }
    public string Provenance => Selected is { } target ? $"{target.Suggestion.Provider} / {target.Suggestion.Model}\nImported {target.Suggestion.ImportedUtc.ToLocalTime():g}\nRun: {target.Suggestion.RunFingerprint}" : "Select a saved proposal.";
    public string Evidence => Selected?.Suggestion.Evidence ?? "";
    public string ImportNotice => Selected is { } target && target.CurrentTags != target.Suggestion.TagsAtImport
        ? "Tags have changed since import. Current values show the latest tags." : "Filename tags remain authoritative. AI suggestions require your review.";
    public string ReviewHistory => Selected?.Receipt is { } r
        ? $"{r.Outcome} · {r.ReviewedUtc.ToLocalTime():g}\nSaved result: {Format(r.After)}\nDecisions: gender {r.Decisions.Gender.Action}, role {r.Decisions.Role.Action}, style {r.Decisions.Style.Action}, race {r.Decisions.Race.Action}.\nThis receipt records the review; later edits and renames may differ." : "";
    private static string Format(TagSet tags) => string.Join(" / ", new[] { tags.Gender, tags.Role, tags.WeaponOrStyle, tags.Race }.Select(t => t ?? "unresolved"));
    public AiReviewPlan Plan => Fields.Count == 4 ? new(Fields[0].Selected.Decision, Fields[1].Selected.Decision, Fields[2].Selected.Decision, Fields[3].Selected.Decision) : AiReviewPlan.KeepAll;
    public string ValidationMessage
    {
        get
        {
            if (Selected is null) return "Scan a folder and import proposals to begin.";
            if (Selected.Receipt is not null) return "Reviewed proposals are read-only. Further edits use the Library's normal tag controls.";
            try
            {
                var tags = Plan.Resolve(Selected.CurrentTags, Selected.Suggestion.Tags);
                if (!Plan.HasDecision) return "Choose at least one field decision, or dismiss this proposal.";
                if (NeedsReplaceConfirmation && !ReplaceConfirmed) return "Confirm replacement of existing tags below.";
                return $"Result: {Format(tags)}. Saves temporary tags; filenames change only through Rename Selected.";
            }
            catch (InvalidDataException) { return "Role and weapon/style must agree. Choose a compatible style or leave style unresolved; fields are never silently cleared."; }
        }
    }
    public bool NeedsReplaceConfirmation
    {
        get
        {
            try { return Selected is { } t && AiReviewPlan.ReplacesExisting(t.CurrentTags, t.FilenameTags, Plan.Resolve(t.CurrentTags, t.Suggestion.Tags)); }
            catch (InvalidDataException) { return false; }
        }
    }
    public bool CanSave
    {
        get
        {
            if (!Editable || !_previewVerified || !Plan.HasDecision) return false;
            try { Plan.Resolve(Selected!.CurrentTags, Selected.Suggestion.Tags); return !NeedsReplaceConfirmation || ReplaceConfirmed; }
            catch (InvalidDataException) { return false; }
        }
    }
    private void FieldChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    { ReplaceConfirmed = false; RaiseStates(); }
    private void RaiseStates()
    {
        OnPropertyChanged(nameof(CanSave)); OnPropertyChanged(nameof(ValidationMessage)); OnPropertyChanged(nameof(NeedsReplaceConfirmation));
        RefreshCommand?.RaiseCanExecuteChanged(); SaveCommand?.RaiseCanExecuteChanged(); DismissCommand?.RaiseCanExecuteChanged();
    }
    private void Filter()
    {
        var id = Selected?.Suggestion.Id;
        Proposals.Clear();
        foreach (var target in _all.Where(t => IncludeReviewed || t.Receipt is null)) Proposals.Add(target);
        Selected = Proposals.FirstOrDefault(t => t.Suggestion.Id == id) ?? Proposals.FirstOrDefault();
        OnPropertyChanged(nameof(CountText));
    }
    public async Task RefreshAsync()
    {
        Busy = true;
        try
        {
            _all = await _workspace.LoadAiReviewsAsync();
            // Force a new revision snapshot and reset field choices even when proposal equality is unchanged.
            Selected = null;
            Filter();
            Message = _all.Count == 0 ? "No saved proposals for this scan. Import suggestions through the agent queue first." : "Choose a proposal. Every field starts with Keep current.";
        }
        catch (Exception ex) { Selected = null; Proposals.Clear(); _all = []; OnPropertyChanged(nameof(CountText)); Message = $"Cannot load reviews: {ex.Message}"; }
        finally { Busy = false; }
    }
    private async Task LoadPreviewAsync(AiReviewTarget target, int generation)
    {
        await _previewGate.WaitAsync();
        try
        {
            if (generation != _selectionGeneration) return;
            var pixels = await _workspace.LoadAiReviewPreviewAsync(target);
            if (generation != _selectionGeneration) return;
            using var stream = new MemoryStream(Convert.FromBase64String(pixels.Base64));
            var bitmap = new BitmapImage();
            bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze();
            Preview = bitmap;
            _previewVerified = true;
            Message = "Image verified against the proposal's source. Review the fields before saving.";
        }
        catch (Exception ex) { if (generation == _selectionGeneration) Message = $"Preview unavailable: {ex.Message}"; }
        finally { _previewGate.Release(); if (generation == _selectionGeneration) RaiseStates(); }
    }
    public void Close() { _selectionGeneration++; Preview = null; }
    public async Task SaveAsync(bool dismiss)
    {
        if (Selected is not { } target || Busy || target.Receipt is not null || (!dismiss && !CanSave)) return;
        Busy = true;
        var message = "";
        try
        {
            await _workspace.SaveAiReviewAsync(target, Plan, ReplaceConfirmed, dismiss);
            message = dismiss ? "Proposal dismissed. Tags are unchanged." : "Review saved as temporary tags. Use Preview Changed in the main window to review filenames.";
            await RefreshAsync();
        }
        catch (Exception ex) { _previewVerified = false; message = ex.Message; }
        finally { Busy = false; Message = message; }
    }
}
