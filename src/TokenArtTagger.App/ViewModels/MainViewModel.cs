using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Data;
using TokenArtTagger.App.Services;
using TokenArtTagger.Core;
using WinForms = System.Windows.Forms;

namespace TokenArtTagger.App.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private readonly ThumbnailService _thumbnailService = new();
    private readonly WorkInProgressTagStore _workInProgressStore = WorkInProgressTagStore.CreateDefault();
    private string _folderPath = string.Empty;
    private string _filterText = string.Empty;
    private string _appliedFilterText = string.Empty;
    private string _statusMessage = "Choose a folder to begin.";
    private string _renameBlockReason = "Select one or more images before previewing or renaming.";
    private bool _isScanReminderActive;
    private bool _isStatusWarning;
    private int _statusWarningGeneration;
    private RenamePreview? _lastPreview;
    private RenamePreviewScope? _lastPreviewScope;
    private CancellationTokenSource? _previewCancellation;
    private CancellationTokenSource? _filterRefreshCancellation;
    private BucketPassOptionViewModel _selectedBucketPass;
    private BucketFilterMode _selectedBucketFilter = BucketFilterMode.MissingOnly;
    private int _bucketPageSize = 100;
    private int _bucketPageIndex;
    private string? _selectedDefaultBucketValue;
    private BucketWorkingSession? _bucketWorkingSession;
    private bool _isBucketModeActive;

    public MainViewModel()
    {
        ItemsView = CollectionViewSource.GetDefaultView(Items);
        ItemsView.Filter = FilterItem;
        TagButtons = new ObservableCollection<TagButtonViewModel>(
            TagSchema.TagButtons().Select(tag => new TagButtonViewModel(tag.Category, tag.Value, tag.Group)));
        TagButtonGroups = new ObservableCollection<TagButtonGroupViewModel>(
            TagButtons
                .GroupBy(tag => tag.Group)
                .Select(group => new TagButtonGroupViewModel(group.Key, group.ToList())));
        TagButtonsView = CollectionViewSource.GetDefaultView(TagButtons);

        BucketPasses = new ObservableCollection<BucketPassOptionViewModel>(
            Enum.GetValues<BucketPass>().Select(pass => new BucketPassOptionViewModel(pass, BucketPassDefinition.For(pass).DisplayName)));
        _selectedBucketPass = BucketPasses[0];
        BucketFilterModes = new ObservableCollection<BucketFilterMode>(Enum.GetValues<BucketFilterMode>());
        BucketPageSizes = new ObservableCollection<int>([50, 100, 200]);

        BrowseCommand = new RelayCommand(_ => BrowseFolder());
        ScanCommand = new AsyncRelayCommand(_ => ScanAsync(), _ => !string.IsNullOrWhiteSpace(FolderPath));
        ApplyTagCommand = new RelayCommand(parameter => ApplyTag((TagButtonViewModel)parameter!), parameter => parameter is TagButtonViewModel);
        PreviewRenameCommand = new AsyncRelayCommand(_ => PreviewRenameAsync(RenamePreviewScope.Selected), _ => ActiveSelectedViewModels().Count > 0);
        PreviewChangedCommand = new AsyncRelayCommand(_ => PreviewRenameAsync(RenamePreviewScope.Changed), _ => Items.Any(item => item.IsDirty));
        CancelPreviewCommand = new RelayCommand(_ => CancelPreview(), _ => _previewCancellation is not null);
        RenameSelectedCommand = new AsyncRelayCommand(_ => RenameSelectedAsync(), _ => RenameReadiness.Evaluate(CurrentSelectedItems()).CanPreview);
        UndoLastBatchCommand = new RelayCommand(_ => SetStatus($"Undo is not implemented in {AppInfo.Version}. Use the JSON undo log for manual recovery.", isWarning: true));
        ApplyBucketCommand = new RelayCommand(parameter => ApplyBucket((BucketDefinitionViewModel)parameter!), parameter => parameter is BucketDefinitionViewModel);
        ApplyDefaultToPageCommand = new RelayCommand(_ => ApplyDefaultToCurrentPage(), _ => BucketPageItems.Count > 0 && SelectedDefaultBucketValue is not null);
        RefreshBucketWorkingSetCommand = new RelayCommand(_ => RebuildBucketWorkingSession(), _ => Items.Count > 0);
        ClearSelectedTemporaryTagsCommand = new RelayCommand(_ => ClearSelectedTemporaryTags(), _ => SelectedItems.Count > 0);
        ClearBucketSelectedTemporaryTagsCommand = new RelayCommand(_ => ClearBucketSelectedTemporaryTags(), _ => BucketSelectedItems.Count > 0);
        ClearCurrentBucketPassTagsCommand = new RelayCommand(_ => ClearCurrentBucketPassTags(), _ => Items.Count > 0);
        ClearAllTemporaryTagsCommand = new RelayCommand(_ => ClearAllTemporaryTags(), _ => Items.Any(item => item.IsDirty));
        NextBucketPageCommand = new RelayCommand(_ => MoveBucketPage(1), _ => BucketPageIndex + 1 < BucketPageCount);
        PreviousBucketPageCommand = new RelayCommand(_ => MoveBucketPage(-1), _ => BucketPageIndex > 0);
        InvertBucketSelectionCommand = new RelayCommand(_ => RequestBucketSelectionAction?.Invoke(BucketSelectionAction.Invert), _ => BucketPageItems.Count > 0);
        SelectUntaggedBucketCommand = new RelayCommand(_ => RequestBucketSelectionAction?.Invoke(BucketSelectionAction.SelectUntagged), _ => BucketPageItems.Count > 0);

        RefreshBucketDefinitions();
    }

    public event Action<BucketSelectionAction>? RequestBucketSelectionAction;

    public DebugEventLogger? DebugLog { get; set; }

    public string AppTitle => AppInfo.WindowTitle;

    public ObservableCollection<ImageItemViewModel> Items { get; } = [];

    public ObservableCollection<ImageItemViewModel> SelectedItems { get; } = [];

    public ObservableCollection<ImageItemViewModel> BucketPageItems { get; } = [];

    public ObservableCollection<ImageItemViewModel> BucketSelectedItems { get; } = [];

    public ObservableCollection<TagButtonViewModel> TagButtons { get; }

    public ObservableCollection<TagButtonGroupViewModel> TagButtonGroups { get; }

    public ICollectionView TagButtonsView { get; }

    public ICollectionView ItemsView { get; }

    public ObservableCollection<BucketPassOptionViewModel> BucketPasses { get; }

    public ObservableCollection<BucketFilterMode> BucketFilterModes { get; }

    public ObservableCollection<int> BucketPageSizes { get; }

    public ObservableCollection<BucketDefinitionViewModel> BucketBuckets { get; } = [];

    public ImageItemViewModel? SelectedItem => SelectedItems.LastOrDefault();

    public ImageItemViewModel? SingleSelectionDetailItem => SelectedItems.Count == 1 ? SelectedItem : null;

    public ImageItemViewModel? BucketSelectedItem => BucketSelectedItems.LastOrDefault();

    public ImageItemViewModel? BucketSingleSelectionDetailItem => BucketSelectedItems.Count == 1 ? BucketSelectedItem : null;

    public string SelectionCountText => SelectedItems.Count == 1
        ? "Selected: 1 image"
        : $"Selected: {SelectedItems.Count} images";

    public string BucketSelectionCountText => BucketSelectedItems.Count == 1
        ? "Selected: 1 image"
        : $"Selected: {BucketSelectedItems.Count} images";

    public string SelectionAggregateText => BuildSelectionAggregate(SelectedItems);

    public string BucketSelectionAggregateText => BuildSelectionAggregate(BucketSelectedItems);

    public string BucketWorkingSetSummary => BuildBucketWorkingSetSummary();

    public string BucketPageText => BucketPageCount == 0
        ? "Page 0 of 0"
        : $"Page {BucketPageIndex + 1} of {BucketPageCount}";

    public int BucketPageCount { get; private set; }

    public string FolderPath
    {
        get => _folderPath;
        set
        {
            if (SetProperty(ref _folderPath, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public string FilterText
    {
        get => _filterText;
        set
        {
            if (SetProperty(ref _filterText, value))
            {
                QueueFilterRefresh();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public bool IsStatusWarning
    {
        get => _isStatusWarning;
        private set => SetProperty(ref _isStatusWarning, value);
    }

    public bool IsScanReminderActive
    {
        get => _isScanReminderActive;
        private set => SetProperty(ref _isScanReminderActive, value);
    }

    public string RenameBlockReason
    {
        get => _renameBlockReason;
        private set => SetProperty(ref _renameBlockReason, value);
    }

    public BucketPassOptionViewModel SelectedBucketPass
    {
        get => _selectedBucketPass;
        set
        {
            if (SetProperty(ref _selectedBucketPass, value))
            {
                BucketPageIndex = 0;
                RefreshBucketDefinitions();
                RebuildBucketWorkingSession();
            }
        }
    }

    public BucketFilterMode SelectedBucketFilter
    {
        get => _selectedBucketFilter;
        set
        {
            if (SetProperty(ref _selectedBucketFilter, value))
            {
                BucketPageIndex = 0;
                RebuildBucketWorkingSession();
            }
        }
    }

    public int BucketPageSize
    {
        get => _bucketPageSize;
        set
        {
            if (SetProperty(ref _bucketPageSize, value))
            {
                BucketPageIndex = 0;
                RefreshBucketPage();
            }
        }
    }

    public int BucketPageIndex
    {
        get => _bucketPageIndex;
        private set
        {
            if (SetProperty(ref _bucketPageIndex, value))
            {
                OnPropertyChanged(nameof(BucketPageText));
            }
        }
    }

    public string? SelectedDefaultBucketValue
    {
        get => _selectedDefaultBucketValue;
        set
        {
            if (SetProperty(ref _selectedDefaultBucketValue, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public RelayCommand BrowseCommand { get; }

    public AsyncRelayCommand ScanCommand { get; }

    public RelayCommand ApplyTagCommand { get; }

    public AsyncRelayCommand PreviewRenameCommand { get; }

    public AsyncRelayCommand PreviewChangedCommand { get; }

    public RelayCommand CancelPreviewCommand { get; }

    public AsyncRelayCommand RenameSelectedCommand { get; }

    public RelayCommand UndoLastBatchCommand { get; }

    public RelayCommand ApplyBucketCommand { get; }

    public RelayCommand ApplyDefaultToPageCommand { get; }

    public RelayCommand RefreshBucketWorkingSetCommand { get; }

    public RelayCommand ClearSelectedTemporaryTagsCommand { get; }

    public RelayCommand ClearBucketSelectedTemporaryTagsCommand { get; }

    public RelayCommand ClearCurrentBucketPassTagsCommand { get; }

    public RelayCommand ClearAllTemporaryTagsCommand { get; }

    public RelayCommand NextBucketPageCommand { get; }

    public RelayCommand PreviousBucketPageCommand { get; }

    public RelayCommand InvertBucketSelectionCommand { get; }

    public RelayCommand SelectUntaggedBucketCommand { get; }

    public async Task ScanAsync()
    {
        using var logScope = DebugLog?.Enter("ScanAsync", selected: SelectionSummary(), details: new Dictionary<string, string?>
        {
            ["folderName"] = DebugEventLogger.SafeNameFromPath(FolderPath)
        });
        IsScanReminderActive = false;
        SetStatus("Scanning...");
        _lastPreview = null;
        _lastPreviewScope = null;
        Items.Clear();
        SelectedItems.Clear();
        BucketSelectedItems.Clear();
        BucketPageItems.Clear();
        _bucketWorkingSession = null;
        _thumbnailService.ClearMemory();

        var result = await FileScanner.ScanAsync(FolderPath);
        var workInProgress = await LoadWorkInProgressMapAsync();
        foreach (var scannedItem in result.Items.OrderBy(item => item.FileName, StringComparer.OrdinalIgnoreCase))
        {
            var item = ApplyWorkInProgress(scannedItem, workInProgress);
            Items.Add(new ImageItemViewModel(item));
        }

        RebuildBucketWorkingSession();
        var legacyUndoWarning = UndoLogService.HasLegacyUndoFolder(FolderPath)
            ? $" Legacy undo folder found at {UndoLogService.LegacyUndoFolderName}; new undo logs now go to app data."
            : string.Empty;
        var parseFailures = Items.Count(item => !item.Item.IsRecognized);
        var parseWarning = parseFailures > 0
            ? $" {parseFailures} filename(s) need tags or parser review."
            : string.Empty;
        DebugLog?.Write("ScanCompleted", details: new Dictionary<string, string?>
        {
            ["count"] = Items.Count.ToString(),
            ["errors"] = result.Errors.Count.ToString(),
            ["parseFailures"] = parseFailures.ToString()
        });
        SetStatus(result.Errors.Count == 0
            ? $"Scanned {Items.Count} images.{parseWarning}{legacyUndoWarning}"
            : $"Scanned {Items.Count} images with {result.Errors.Count} errors.{parseWarning}{legacyUndoWarning}",
            isWarning: result.Errors.Count > 0 || legacyUndoWarning.Length > 0 || parseFailures > 0);
        UpdateRenameReadiness();
        RaiseCommandStates();
    }

    public void ReplaceSelection(IEnumerable<ImageItemViewModel> selectedItems)
    {
        var list = selectedItems.ToList();
        using var logScope = DebugLog?.Enter("SelectionChanged", "Library", SelectionSummary(), new Dictionary<string, string?>
        {
            ["incomingCount"] = list.Count.ToString()
        });
        SelectedItems.Clear();
        foreach (var item in list)
        {
            SelectedItems.Add(item);
        }

        _lastPreview = null;
        _lastPreviewScope = null;
        NotifyLibrarySelectionDetailsChanged();
        UpdateRenameReadiness();
        RaiseCommandStates();
    }

    public void ReplaceBucketSelection(IEnumerable<ImageItemViewModel> selectedItems)
    {
        var list = selectedItems.ToList();
        using var logScope = DebugLog?.Enter("SelectionChanged", "Bucket", BucketSelectionSummary(), new Dictionary<string, string?>
        {
            ["incomingCount"] = list.Count.ToString()
        });
        BucketSelectedItems.Clear();
        foreach (var item in list)
        {
            BucketSelectedItems.Add(item);
        }

        NotifyBucketSelectionDetailsChanged();
        UpdateRenameReadiness();
        RaiseCommandStates();
    }

    public void ApplyTag(TagButtonViewModel tag, IEnumerable<ImageItemViewModel>? targets = null)
    {
        var targetList = (targets ?? SelectedItems).Distinct().ToList();
        if (targetList.Count == 0)
        {
            SetStatus("Select one or more images before applying a tag.", isWarning: true);
            return;
        }

        var message = ApplyTagTo(targetList, tag.Category, tag.Value);
        SetStatus(message ?? $"Applied {tag.Value} to {targetList.Count} selected image(s).", message is not null);
        UpdateAfterTagsChanged();
    }

    public async Task LoadThumbnailAsync(ImageItemViewModel item)
    {
        using var logScope = DebugLog?.Enter("ThumbnailLoad", selected: SelectionSummary(), details: new Dictionary<string, string?>
        {
            ["file"] = item.FileName
        });
        if (item.Thumbnail is not null)
        {
            return;
        }

        var thumbnail = await _thumbnailService.LoadThumbnailAsync(item.FullPath);
        System.Windows.Application.Current.Dispatcher.Invoke(() => item.Thumbnail = thumbnail);
    }

    private async Task PreviewRenameAsync(RenamePreviewScope scope)
    {
        using var logScope = DebugLog?.Enter("PreviewRename", selected: SelectionSummary(), details: new Dictionary<string, string?>
        {
            ["scope"] = scope.ToString()
        });
        var targets = scope == RenamePreviewScope.Selected
            ? RenameTargetSelector.Selected(CurrentSelectedItems(), CurrentAllItems())
            : RenameTargetSelector.Dirty(CurrentAllItems());

        var readiness = RenameReadiness.Evaluate(targets);
        if (!readiness.CanPreview)
        {
            SetStatus(readiness.Message, isWarning: true);
            RenameBlockReason = readiness.Message;
            RaiseCommandStates();
            return;
        }

        var label = scope == RenamePreviewScope.Selected ? "selected" : "changed";
        SetStatus($"Building rename preview for {targets.Count} {label} image(s)...");
        foreach (var item in Items)
        {
            item.ApplyPreview(null);
        }

        _previewCancellation?.Dispose();
        _previewCancellation = new CancellationTokenSource();
        CancelPreviewCommand.RaiseCanExecuteChanged();
        var progress = new Progress<RenamePreviewProgress>(step =>
        {
            SetStatus($"Hashing {step.Completed}/{step.Total}: {step.CurrentFileName}");
        });

        try
        {
            _lastPreview = await FileRenamer.BuildPreviewAsync(targets, progress, _previewCancellation.Token);
            _lastPreviewScope = scope;
        }
        catch (OperationCanceledException)
        {
            SetStatus("Rename preview canceled.");
            _lastPreview = null;
            _lastPreviewScope = null;
            return;
        }
        finally
        {
            _previewCancellation?.Dispose();
            _previewCancellation = null;
            CancelPreviewCommand.RaiseCanExecuteChanged();
        }

        foreach (var entry in _lastPreview.Entries)
        {
            Items.FirstOrDefault(item => item.FullPath == entry.Item.FullPath)?.ApplyPreview(entry);
        }

        var blockingErrors = _lastPreview.BlockingErrorCount;
        if (blockingErrors == 0)
        {
            if (_lastPreview.CanRename)
            {
                SetStatus($"Preview ready for {_lastPreview.Entries.Count} {label} image(s).");
            }
            else
            {
                var renameableCount = _lastPreview.Entries.Count(entry => entry.CanRename);
                SetStatus(renameableCount == 0
                    ? $"Preview found {_lastPreview.AlreadyDesiredCount} already-normalized image(s). No rename is needed."
                    : $"Preview includes {_lastPreview.AlreadyDesiredCount} already-normalized image(s). Select only images that need renaming to continue.");
            }
        }
        else
        {
            SetStatus($"Preview found {blockingErrors} issue(s). Fix missing tags or conflicts before renaming.", isWarning: true);
        }

        var hasRenameableEntries = _lastPreview.Entries.Any(entry => entry.CanRename);
        RenameBlockReason = _lastPreview.CanRename
            ? "Ready to rename selected preview."
            : blockingErrors == 0
                ? hasRenameableEntries
                    ? "Some selected images already use the desired hash filename format."
                    : "Selected images already use the desired hash filename format."
                : "Preview has issues.";
        RaiseCommandStates();
    }

    private async Task RenameSelectedAsync()
    {
        if (_lastPreview?.CanRename != true || _lastPreviewScope != RenamePreviewScope.Selected)
        {
            await PreviewRenameAsync(RenamePreviewScope.Selected);
            if (_lastPreview?.CanRename == true)
            {
                SetStatus("Preview ready. Review the proposed filenames, then click Rename Selected again to confirm.");
            }

            return;
        }

        var confirmation = System.Windows.MessageBox.Show(
            $"Rename {_lastPreview.Entries.Count} selected file(s) in place?",
            "Confirm Rename",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirmation != MessageBoxResult.Yes)
        {
            SetStatus("Rename canceled.");
            return;
        }

        var renamedPaths = _lastPreview.Entries
            .Where(entry => entry.CanRename && entry.ProposedPath is not null)
            .Select(entry => entry.ProposedPath!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = await FileRenamer.RenameAsync(_lastPreview, UndoLogService.DefaultUndoLogFolder());
        SetStatus(result.Errors.Count == 0
            ? $"Renamed {result.RenamedCount} file(s). Undo log: {result.UndoLogPath}"
            : $"Renamed {result.RenamedCount} file(s) with {result.Errors.Count} error(s). Undo log: {result.UndoLogPath}",
            isWarning: result.Errors.Count > 0);

        await ScanAsync();
        var stillSelected = Items.Where(item => renamedPaths.Contains(item.FullPath)).ToList();
        ReplaceSelection(stillSelected);
    }

    public void ApplyBucket(BucketDefinitionViewModel bucket, IEnumerable<ImageItemViewModel>? targets = null)
    {
        var targetList = (targets ?? BucketSelectedItems).Distinct().ToList();
        if (targetList.Count == 0)
        {
            SetStatus("Select one or more bucket images before applying a bucket.", isWarning: true);
            return;
        }

        var message = ApplyTagTo(targetList, bucket.Category, bucket.Value);
        SetStatus(message ?? $"Applied {bucket.Category}={bucket.Value} to {targetList.Count} image(s).", message is not null);
        UpdateAfterTagsChanged();
    }

    private void ApplyDefaultToCurrentPage()
    {
        if (SelectedDefaultBucketValue is null || BucketPageItems.Count == 0)
        {
            return;
        }

        var preview = BucketRemainderTagger.ApplyDefault(
            BucketPageItems.Select(item => item.Item).ToList(),
            SelectedBucketPass.Pass,
            SelectedDefaultBucketValue);

        if (preview.ChangedCount == 0)
        {
            SetStatus("No remaining unassigned images on this page.", isWarning: true);
            return;
        }

        var confirmation = System.Windows.MessageBox.Show(
            $"Apply {SelectedDefaultBucketValue} to {preview.ChangedCount} remaining image(s) on this page?",
            "Apply Default To Page",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirmation != MessageBoxResult.Yes)
        {
            SetStatus("Default-to-page canceled.");
            return;
        }

        foreach (var updated in preview.Items)
        {
            Items.FirstOrDefault(item => item.FullPath == updated.FullPath)?.ReplaceItem(updated);
        }

        SetStatus($"Applied {SelectedDefaultBucketValue} to {preview.ChangedCount} remaining image(s) on this page.");
        UpdateAfterTagsChanged();
    }

    private void ClearSelectedTemporaryTags()
    {
        if (SelectedItems.Count == 0)
        {
            SetStatus("Select one or more images before clearing temporary tags.", isWarning: true);
            return;
        }

        ResetViewModels(TemporaryTagReset.Reset(CurrentSelectedItems()));
        SetStatus($"Cleared temporary tags from {SelectedItems.Count} selected image(s).");
        UpdateAfterTagsChanged();
    }

    private void ClearBucketSelectedTemporaryTags()
    {
        if (BucketSelectedItems.Count == 0)
        {
            SetStatus("Select one or more bucket images before clearing temporary tags.", isWarning: true);
            return;
        }

        ResetViewModels(TemporaryTagReset.Reset(BucketSelectedItems.Select(item => item.Item)));
        SetStatus($"Cleared temporary tags from {BucketSelectedItems.Count} selected bucket image(s).");
        UpdateAfterTagsChanged();
    }

    private void ClearCurrentBucketPassTags()
    {
        var targets = TemporaryTagReset.ResetCurrentPass(CurrentAllItems(), SelectedBucketPass.Pass, SelectedBucketFilter);
        if (targets.Count == 0)
        {
            SetStatus("No temporary tags match the current bucket pass and filter.", isWarning: true);
            return;
        }

        if (!ConfirmReset($"Clear temporary tags for {targets.Count} image(s) in the current bucket pass?"))
        {
            SetStatus("Clear bucket pass canceled.");
            return;
        }

        ResetViewModels(targets);
        SetStatus($"Cleared temporary tags from {targets.Count} current bucket pass image(s).");
        UpdateAfterTagsChanged();
    }

    private void ClearAllTemporaryTags()
    {
        var dirtyItems = Items.Where(item => item.IsDirty).Select(item => item.Item).ToList();
        if (dirtyItems.Count == 0)
        {
            SetStatus("There are no temporary tags to clear.", isWarning: true);
            return;
        }

        if (!ConfirmReset($"Clear all temporary tags for {dirtyItems.Count} changed image(s) in the current library?"))
        {
            SetStatus("Clear all temporary tags canceled.");
            return;
        }

        ResetViewModels(TemporaryTagReset.Reset(dirtyItems));
        SetStatus($"Cleared all temporary tags from {dirtyItems.Count} image(s).");
        UpdateAfterTagsChanged();
    }

    private static bool ConfirmReset(string message)
    {
        return System.Windows.MessageBox.Show(
            $"{message}\n\nImage files will not be changed. Parsed filename tags will still appear.",
            "Clear Temporary Tags",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }

    private void ResetViewModels(IEnumerable<ImageItem> updatedItems)
    {
        foreach (var updated in updatedItems)
        {
            Items.FirstOrDefault(item => item.FullPath == updated.FullPath)?.ReplaceItem(updated);
        }
    }

    private string? ApplyTagTo(IEnumerable<ImageItemViewModel> items, string category, string value)
    {
        string? message = null;
        var list = items.ToList();
        foreach (var item in list)
        {
            var before = item.Item.CurrentTags;
            item.ApplyTag(category, value);
            var after = item.Item.CurrentTags;
            if (category == TagSchema.RoleCategory &&
                !string.IsNullOrWhiteSpace(before.WeaponOrStyle) &&
                string.IsNullOrWhiteSpace(after.WeaponOrStyle) &&
                !string.Equals(value, TagSchema.GenericRole, StringComparison.OrdinalIgnoreCase))
            {
                message = $"Cleared incompatible style '{before.WeaponOrStyle}'; {TagSchema.StyleRequirementMessage(value)}.";
            }
        }

        return message;
    }

    private void UpdateAfterTagsChanged()
    {
        using var logScope = DebugLog?.Enter("TagsChanged", selected: SelectionSummary());
        _lastPreview = null;
        _lastPreviewScope = null;
        _ = SaveWorkInProgressAsync();
        UpdateBucketPassCompletionStates();
        OnPropertyChanged(nameof(BucketWorkingSetSummary));
        NotifyLibrarySelectionDetailsChanged();
        NotifyBucketSelectionDetailsChanged();
        UpdateRenameReadiness();
        RaiseCommandStates();
    }

    private void RefreshBucketDefinitions()
    {
        BucketBuckets.Clear();
        var definition = BucketPassDefinition.For(SelectedBucketPass.Pass);
        foreach (var bucket in definition.Buckets)
        {
            BucketBuckets.Add(new BucketDefinitionViewModel(bucket.Category, bucket.Value, bucket.Label));
        }

        SelectedDefaultBucketValue = BucketBuckets.FirstOrDefault()?.Value;
    }

    private void RebuildBucketWorkingSession()
    {
        using var logScope = DebugLog?.Enter("RebuildBucketWorkingSet", "Bucket", BucketSelectionSummary(), new Dictionary<string, string?>
        {
            ["pass"] = SelectedBucketPass.DisplayName,
            ["filter"] = SelectedBucketFilter.ToString()
        });
        _bucketWorkingSession = BucketWorkingSession.Create(
            CurrentAllItems(),
            SelectedBucketPass.Pass,
            SelectedBucketFilter);
        BucketPageIndex = 0;
        RefreshBucketPage();
    }

    private void RefreshBucketPage()
    {
        using var logScope = DebugLog?.Enter("RefreshBucketPage", "Bucket", BucketSelectionSummary(), new Dictionary<string, string?>
        {
            ["pass"] = SelectedBucketPass.DisplayName,
            ["filter"] = SelectedBucketFilter.ToString(),
            ["pageSize"] = BucketPageSize.ToString(),
            ["searchLength"] = _appliedFilterText.Length.ToString()
        });
        _bucketWorkingSession ??= BucketWorkingSession.Create(
            CurrentAllItems(),
            SelectedBucketPass.Pass,
            SelectedBucketFilter);
        var workingSet = _bucketWorkingSession.Materialize(CurrentAllItems(), _appliedFilterText);
        BucketPageCount = workingSet.Count == 0 ? 0 : (int)Math.Ceiling(workingSet.Count / (double)BucketPageSize);
        if (BucketPageIndex >= BucketPageCount)
        {
            BucketPageIndex = Math.Max(0, BucketPageCount - 1);
        }

        BucketPageItems.Clear();
        foreach (var item in workingSet.Skip(BucketPageIndex * BucketPageSize).Take(BucketPageSize))
        {
            var viewModel = Items.FirstOrDefault(candidate => candidate.FullPath == item.FullPath);
            if (viewModel is not null)
            {
                BucketPageItems.Add(viewModel);
            }
        }

        BucketSelectedItems.Clear();
        OnPropertyChanged(nameof(BucketPageCount));
        OnPropertyChanged(nameof(BucketPageText));
        OnPropertyChanged(nameof(BucketWorkingSetSummary));
        UpdateBucketPassCompletionStates();
        NotifyBucketSelectionDetailsChanged();
        RaiseCommandStates();
    }

    private void MoveBucketPage(int offset)
    {
        BucketPageIndex = Math.Clamp(BucketPageIndex + offset, 0, Math.Max(0, BucketPageCount - 1));
        RefreshBucketPage();
    }

    public bool IsMissingForCurrentBucketPass(ImageItemViewModel item)
    {
        return BucketWorkingSet.Filter([item.Item], SelectedBucketPass.Pass, BucketFilterMode.MissingOnly).Count > 0;
    }

    public void SetBucketModeActive(bool isBucketModeActive)
    {
        if (_isBucketModeActive == isBucketModeActive)
        {
            return;
        }

        _isBucketModeActive = isBucketModeActive;
        if (isBucketModeActive)
        {
            RebuildBucketWorkingSession();
        }
        else
        {
            _bucketWorkingSession = null;
            BucketPageItems.Clear();
            BucketSelectedItems.Clear();
            UpdateBucketPassCompletionStates();
            NotifyBucketSelectionDetailsChanged();
            OnPropertyChanged(nameof(BucketWorkingSetSummary));
        }

        UpdateRenameReadiness();
        RaiseCommandStates();
    }

    public void ShowWarning(string message)
    {
        SetStatus(message, isWarning: true);
    }

    public void ShowInfo(string message)
    {
        SetStatus(message);
    }

    private async Task<Dictionary<string, WorkInProgressTagRecord>> LoadWorkInProgressMapAsync()
    {
        var records = await _workInProgressStore.LoadAsync();
        return records.ToDictionary(record => record.IdentityKey, StringComparer.OrdinalIgnoreCase);
    }

    private ImageItem ApplyWorkInProgress(ImageItem item, Dictionary<string, WorkInProgressTagRecord> records)
    {
        try
        {
            var identity = FileIdentity.FromPath(item.FullPath);
            return records.TryGetValue(identity.Key, out var record)
                ? item with { CurrentTags = record.Tags }
                : item;
        }
        catch (IOException)
        {
            return item;
        }
    }

    private async Task SaveWorkInProgressAsync()
    {
        var records = new List<WorkInProgressTagRecord>();
        foreach (var item in Items.Where(item => item.IsDirty))
        {
            try
            {
                records.Add(new WorkInProgressTagRecord(FileIdentity.FromPath(item.FullPath).Key, item.Item.CurrentTags));
            }
            catch (IOException)
            {
                // Ignore unavailable files; scanning/renaming will report IO issues separately.
            }
        }

        if (records.Count == 0)
        {
            await _workInProgressStore.ClearAsync();
        }
        else
        {
            await _workInProgressStore.SaveAsync(records);
        }
    }

    private void BrowseFolder()
    {
        using var logScope = DebugLog?.Enter("BrowseFolder");
        using var dialog = new WinForms.FolderBrowserDialog
        {
            Description = "Choose the root image folder to scan",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(FolderPath) ? FolderPath : string.Empty
        };

        if (dialog.ShowDialog() == WinForms.DialogResult.OK)
        {
            FolderPath = dialog.SelectedPath;
            DebugLog?.Write("FolderSelected", details: new Dictionary<string, string?>
            {
                ["folderName"] = DebugEventLogger.SafeNameFromPath(FolderPath),
                ["exists"] = Directory.Exists(FolderPath).ToString()
            });
            IsScanReminderActive = true;
            SetStatus("Folder selected. Click Scan Folder to load images.");
            RaiseCommandStates();
        }
    }

    private void CancelPreview()
    {
        _previewCancellation?.Cancel();
    }

    private bool FilterItem(object candidate)
    {
        if (candidate is not ImageItemViewModel item || string.IsNullOrWhiteSpace(_appliedFilterText))
        {
            return true;
        }

        return ImageSearchMatcher.Matches(item.Item, _appliedFilterText);
    }

    private void QueueFilterRefresh()
    {
        _filterRefreshCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _filterRefreshCancellation = cancellation;
        _ = ApplyFilterAfterDelayAsync(cancellation);
    }

    private async Task ApplyFilterAfterDelayAsync(CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(300), cancellation.Token);
            var filterText = FilterText;
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                using var logScope = DebugLog?.Enter("SearchFilterApply", selected: SelectionSummary(), details: new Dictionary<string, string?>
                {
                    ["filterLength"] = filterText.Length.ToString()
                });
                if (cancellation.IsCancellationRequested)
                {
                    return;
                }

                _appliedFilterText = filterText;
                ItemsView.Refresh();
                RefreshBucketPage();
            });
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_filterRefreshCancellation, cancellation))
            {
                _filterRefreshCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    private void RaiseCommandStates()
    {
        using var logScope = DebugLog?.Enter("RaiseCommandStates", selected: SelectionSummary());
        ScanCommand.RaiseCanExecuteChanged();
        PreviewRenameCommand.RaiseCanExecuteChanged();
        PreviewChangedCommand.RaiseCanExecuteChanged();
        CancelPreviewCommand.RaiseCanExecuteChanged();
        RenameSelectedCommand.RaiseCanExecuteChanged();
        ApplyDefaultToPageCommand.RaiseCanExecuteChanged();
        RefreshBucketWorkingSetCommand.RaiseCanExecuteChanged();
        ClearSelectedTemporaryTagsCommand.RaiseCanExecuteChanged();
        ClearBucketSelectedTemporaryTagsCommand.RaiseCanExecuteChanged();
        ClearCurrentBucketPassTagsCommand.RaiseCanExecuteChanged();
        ClearAllTemporaryTagsCommand.RaiseCanExecuteChanged();
        NextBucketPageCommand.RaiseCanExecuteChanged();
        PreviousBucketPageCommand.RaiseCanExecuteChanged();
        InvertBucketSelectionCommand.RaiseCanExecuteChanged();
        SelectUntaggedBucketCommand.RaiseCanExecuteChanged();
    }

    private IReadOnlyList<ImageItem> CurrentSelectedItems()
    {
        return ActiveSelectedViewModels().Select(item => item.Item).ToList();
    }

    private IReadOnlyList<ImageItem> CurrentAllItems()
    {
        return Items.Select(item => item.Item).ToList();
    }

    private void UpdateRenameReadiness()
    {
        using var logScope = DebugLog?.Enter("ValidationUpdate", selected: SelectionSummary());
        var readiness = RenameReadiness.Evaluate(CurrentSelectedItems());
        RenameBlockReason = readiness.CanPreview ? "Ready to preview selected images." : readiness.Message;
    }

    private IReadOnlyList<ImageItemViewModel> ActiveSelectedViewModels()
    {
        return _isBucketModeActive
            ? BucketSelectedItems.ToList()
            : SelectedItems.ToList();
    }

    private void UpdateBucketPassCompletionStates()
    {
        foreach (var item in Items)
        {
            item.IsCompleteForCurrentBucketPass = _isBucketModeActive &&
                _bucketWorkingSession?.Contains(item.Item) == true &&
                BucketWorkingSet.IsCompleteForPass(item.Item, SelectedBucketPass.Pass);
        }
    }

    private string BuildBucketWorkingSetSummary()
    {
        if (_bucketWorkingSession is null)
        {
            return $"{SelectedBucketPass.DisplayName} pass - no active working set.";
        }

        var allItems = CurrentAllItems();
        return $"{SelectedBucketPass.DisplayName} - {SelectedBucketFilter} working set: {_bucketWorkingSession.Count} images" +
            $"{Environment.NewLine}Visible after search: {_bucketWorkingSession.Materialize(allItems, _appliedFilterText).Count}" +
            $"{Environment.NewLine}Completed this pass: {_bucketWorkingSession.CompletedThisPassCount(allItems)}" +
            $"{Environment.NewLine}Ready to rename: {_bucketWorkingSession.ReadyToRenameCount(allItems)}";
    }

    private void NotifyLibrarySelectionDetailsChanged()
    {
        using (DebugLog?.Enter("SelectedItemDetailsUpdate", "Library", SelectionSummary()))
        {
            OnPropertyChanged(nameof(SelectedItem));
            OnPropertyChanged(nameof(SingleSelectionDetailItem));
            OnPropertyChanged(nameof(SelectionCountText));
            OnPropertyChanged(nameof(SelectionAggregateText));
        }
    }

    private void NotifyBucketSelectionDetailsChanged()
    {
        using (DebugLog?.Enter("SelectedItemDetailsUpdate", "Bucket", BucketSelectionSummary()))
        {
            OnPropertyChanged(nameof(BucketSelectedItem));
            OnPropertyChanged(nameof(BucketSingleSelectionDetailItem));
            OnPropertyChanged(nameof(BucketSelectionCountText));
            OnPropertyChanged(nameof(BucketSelectionAggregateText));
        }
    }

    private static string BuildSelectionAggregate(IEnumerable<ImageItemViewModel> selection)
    {
        var items = selection.ToList();
        if (items.Count == 0)
        {
            return "No image selected.";
        }

        if (items.Count == 1)
        {
            return "Selected image details below.";
        }

        var changed = items.Count(item => item.IsDirty);
        var invalid = items.Count(item => item.HasInvalidTags);
        var incomplete = items.Count(item => item.HasIncompleteTags);

        return string.Join(Environment.NewLine, [
            $"Selected: {items.Count} images",
            $"Changed: {changed}",
            $"Invalid: {invalid}",
            $"Incomplete: {incomplete}",
            $"Common tags: {BuildCommonTagsText(items)}"
        ]);
    }

    private static string BuildCommonTagsText(IReadOnlyCollection<ImageItemViewModel> items)
    {
        var common = new[]
        {
            CommonTag("gender", items.Select(item => item.Item.CurrentTags.Gender)),
            CommonTag("role", items.Select(item => item.Item.CurrentTags.Role)),
            CommonTag("style", items.Select(item => item.Item.CurrentTags.WeaponOrStyle)),
            CommonTag("race", items.Select(item => item.Item.CurrentTags.Race))
        }.Where(value => value is not null);

        return string.Join("; ", common) is { Length: > 0 } tags ? tags : "none";
    }

    private static string? CommonTag(string label, IEnumerable<string?> values)
    {
        var normalized = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return normalized.Count == 1 ? $"{label}={normalized[0]}" : null;
    }

    private void SetStatus(string message, bool isWarning = false)
    {
        using var logScope = DebugLog?.Enter("StatusUpdate", selected: SelectionSummary(), details: new Dictionary<string, string?>
        {
            ["isWarning"] = isWarning.ToString(),
            ["message"] = message
        });
        StatusMessage = message;
        if (!isWarning)
        {
            IsStatusWarning = false;
            return;
        }

        IsStatusWarning = true;
        var generation = ++_statusWarningGeneration;
        _ = ClearStatusWarningAsync(generation);
    }

    private async Task ClearStatusWarningAsync(int generation)
    {
        await Task.Delay(TimeSpan.FromSeconds(2));
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (generation == _statusWarningGeneration)
            {
                IsStatusWarning = false;
            }
        });
    }

    private string SelectionSummary()
    {
        var selected = SelectedItem;
        return selected is null
            ? $"library={SelectedItems.Count};file=-"
            : $"library={SelectedItems.Count};file={selected.FileName}";
    }

    private string BucketSelectionSummary()
    {
        var selected = BucketSelectedItems.LastOrDefault();
        return selected is null
            ? $"bucket={BucketSelectedItems.Count};file=-"
            : $"bucket={BucketSelectedItems.Count};file={selected.FileName}";
    }
}

public sealed record BucketPassOptionViewModel(BucketPass Pass, string DisplayName)
{
    public override string ToString() => DisplayName;
}

public sealed record BucketDefinitionViewModel(string Category, string Value, string Label);

public sealed record TagButtonGroupViewModel(string Name, IReadOnlyList<TagButtonViewModel> Buttons);

public enum BucketSelectionAction
{
    Invert,
    SelectUntagged
}
