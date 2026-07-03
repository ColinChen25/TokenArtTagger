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
    private string _statusMessage = "Choose a folder to begin.";
    private string _renameBlockReason = "Select one or more images before previewing or renaming.";
    private bool _isStatusWarning;
    private int _statusWarningGeneration;
    private RenamePreview? _lastPreview;
    private RenamePreviewScope? _lastPreviewScope;
    private CancellationTokenSource? _previewCancellation;
    private BucketPassOptionViewModel _selectedBucketPass;
    private BucketFilterMode _selectedBucketFilter = BucketFilterMode.MissingOnly;
    private int _bucketPageSize = 100;
    private int _bucketPageIndex;
    private string? _selectedDefaultBucketValue;

    public MainViewModel()
    {
        ItemsView = CollectionViewSource.GetDefaultView(Items);
        ItemsView.Filter = FilterItem;
        TagButtons = new ObservableCollection<TagButtonViewModel>(
            TagSchema.TagButtons().Select(tag => new TagButtonViewModel(tag.Category, tag.Value, tag.Group)));
        TagButtonsView = CollectionViewSource.GetDefaultView(TagButtons);
        TagButtonsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(TagButtonViewModel.Group)));

        BucketPasses = new ObservableCollection<BucketPassOptionViewModel>(
            Enum.GetValues<BucketPass>().Select(pass => new BucketPassOptionViewModel(pass, BucketPassDefinition.For(pass).DisplayName)));
        _selectedBucketPass = BucketPasses[0];
        BucketFilterModes = new ObservableCollection<BucketFilterMode>(Enum.GetValues<BucketFilterMode>());
        BucketPageSizes = new ObservableCollection<int>([50, 100, 200]);

        BrowseCommand = new RelayCommand(_ => BrowseFolder());
        ScanCommand = new AsyncRelayCommand(_ => ScanAsync(), _ => !string.IsNullOrWhiteSpace(FolderPath));
        ApplyTagCommand = new RelayCommand(parameter => ApplyTag((TagButtonViewModel)parameter!), parameter => parameter is TagButtonViewModel);
        PreviewRenameCommand = new AsyncRelayCommand(_ => PreviewRenameAsync(RenamePreviewScope.Selected), _ => SelectedItems.Count > 0);
        PreviewChangedCommand = new AsyncRelayCommand(_ => PreviewRenameAsync(RenamePreviewScope.Changed), _ => Items.Any(item => item.IsDirty));
        CancelPreviewCommand = new RelayCommand(_ => CancelPreview(), _ => _previewCancellation is not null);
        RenameSelectedCommand = new AsyncRelayCommand(_ => RenameSelectedAsync(), _ => RenameReadiness.Evaluate(CurrentSelectedItems()).CanPreview);
        UndoLastBatchCommand = new RelayCommand(_ => SetStatus($"Undo is not implemented in {AppInfo.Version}. Use the JSON undo log for manual recovery.", isWarning: true));
        ApplyBucketCommand = new RelayCommand(parameter => ApplyBucket((BucketDefinitionViewModel)parameter!), parameter => parameter is BucketDefinitionViewModel);
        ApplyDefaultToPageCommand = new RelayCommand(_ => ApplyDefaultToCurrentPage(), _ => BucketPageItems.Count > 0 && SelectedDefaultBucketValue is not null);
        ClearSelectedTemporaryTagsCommand = new RelayCommand(_ => ClearSelectedTemporaryTags(), _ => SelectedItems.Count > 0);
        ClearCurrentBucketPassTagsCommand = new RelayCommand(_ => ClearCurrentBucketPassTags(), _ => Items.Count > 0);
        ClearAllTemporaryTagsCommand = new RelayCommand(_ => ClearAllTemporaryTags(), _ => Items.Any(item => item.IsDirty));
        NextBucketPageCommand = new RelayCommand(_ => MoveBucketPage(1), _ => BucketPageIndex + 1 < BucketPageCount);
        PreviousBucketPageCommand = new RelayCommand(_ => MoveBucketPage(-1), _ => BucketPageIndex > 0);
        InvertBucketSelectionCommand = new RelayCommand(_ => RequestBucketSelectionAction?.Invoke(BucketSelectionAction.Invert), _ => BucketPageItems.Count > 0);
        SelectUntaggedBucketCommand = new RelayCommand(_ => RequestBucketSelectionAction?.Invoke(BucketSelectionAction.SelectUntagged), _ => BucketPageItems.Count > 0);

        RefreshBucketDefinitions();
    }

    public event Action<BucketSelectionAction>? RequestBucketSelectionAction;

    public string AppTitle => AppInfo.WindowTitle;

    public ObservableCollection<ImageItemViewModel> Items { get; } = [];

    public ObservableCollection<ImageItemViewModel> SelectedItems { get; } = [];

    public ObservableCollection<ImageItemViewModel> BucketPageItems { get; } = [];

    public ObservableCollection<ImageItemViewModel> BucketSelectedItems { get; } = [];

    public ObservableCollection<TagButtonViewModel> TagButtons { get; }

    public ICollectionView TagButtonsView { get; }

    public ICollectionView ItemsView { get; }

    public ObservableCollection<BucketPassOptionViewModel> BucketPasses { get; }

    public ObservableCollection<BucketFilterMode> BucketFilterModes { get; }

    public ObservableCollection<int> BucketPageSizes { get; }

    public ObservableCollection<BucketDefinitionViewModel> BucketBuckets { get; } = [];

    public ImageItemViewModel? SelectedItem => SelectedItems.LastOrDefault();

    public string SelectionCountText => SelectedItems.Count == 1
        ? "Selected: 1 image"
        : $"Selected: {SelectedItems.Count} images";

    public string BucketSelectionCountText => BucketSelectedItems.Count == 1
        ? "Selected: 1 image"
        : $"Selected: {BucketSelectedItems.Count} images";

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
                ItemsView.Refresh();
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
                RefreshBucketPage();
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
                RefreshBucketPage();
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

    public RelayCommand ClearSelectedTemporaryTagsCommand { get; }

    public RelayCommand ClearCurrentBucketPassTagsCommand { get; }

    public RelayCommand ClearAllTemporaryTagsCommand { get; }

    public RelayCommand NextBucketPageCommand { get; }

    public RelayCommand PreviousBucketPageCommand { get; }

    public RelayCommand InvertBucketSelectionCommand { get; }

    public RelayCommand SelectUntaggedBucketCommand { get; }

    public async Task ScanAsync()
    {
        SetStatus("Scanning...");
        _lastPreview = null;
        _lastPreviewScope = null;
        Items.Clear();
        SelectedItems.Clear();
        BucketSelectedItems.Clear();
        BucketPageItems.Clear();
        _thumbnailService.ClearMemory();

        var result = await FileScanner.ScanAsync(FolderPath);
        var workInProgress = await LoadWorkInProgressMapAsync();
        foreach (var scannedItem in result.Items.OrderBy(item => item.FileName, StringComparer.OrdinalIgnoreCase))
        {
            var item = ApplyWorkInProgress(scannedItem, workInProgress);
            Items.Add(new ImageItemViewModel(item));
        }

        RefreshBucketPage();
        var legacyUndoWarning = UndoLogService.HasLegacyUndoFolder(FolderPath)
            ? $" Legacy undo folder found at {UndoLogService.LegacyUndoFolderName}; new undo logs now go to app data."
            : string.Empty;
        SetStatus(result.Errors.Count == 0
            ? $"Scanned {Items.Count} images.{legacyUndoWarning}"
            : $"Scanned {Items.Count} images with {result.Errors.Count} errors.{legacyUndoWarning}",
            isWarning: result.Errors.Count > 0 || legacyUndoWarning.Length > 0);
        UpdateRenameReadiness();
        RaiseCommandStates();
    }

    public void ReplaceSelection(IEnumerable<ImageItemViewModel> selectedItems)
    {
        SelectedItems.Clear();
        foreach (var item in selectedItems)
        {
            SelectedItems.Add(item);
        }

        _lastPreview = null;
        _lastPreviewScope = null;
        OnPropertyChanged(nameof(SelectedItem));
        OnPropertyChanged(nameof(SelectionCountText));
        UpdateRenameReadiness();
        RaiseCommandStates();
    }

    public void ReplaceBucketSelection(IEnumerable<ImageItemViewModel> selectedItems)
    {
        BucketSelectedItems.Clear();
        foreach (var item in selectedItems)
        {
            BucketSelectedItems.Add(item);
        }

        OnPropertyChanged(nameof(BucketSelectionCountText));
        RaiseCommandStates();
    }

    public void ApplyTag(TagButtonViewModel tag)
    {
        if (SelectedItems.Count == 0)
        {
            SetStatus("Select one or more images before applying a tag.", isWarning: true);
            return;
        }

        var message = ApplyTagTo(SelectedItems, tag.Category, tag.Value);
        SetStatus(message ?? $"Applied {tag.Value} to {SelectedItems.Count} selected image(s).", message is not null);
        UpdateAfterTagsChanged();
    }

    public async Task LoadThumbnailAsync(ImageItemViewModel item)
    {
        if (item.Thumbnail is not null)
        {
            return;
        }

        var thumbnail = await _thumbnailService.LoadThumbnailAsync(item.FullPath);
        System.Windows.Application.Current.Dispatcher.Invoke(() => item.Thumbnail = thumbnail);
    }

    private async Task PreviewRenameAsync(RenamePreviewScope scope)
    {
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

        var errors = _lastPreview.Entries.Count(entry => !entry.CanRename);
        SetStatus(errors == 0
            ? $"Preview ready for {_lastPreview.Entries.Count} {label} image(s)."
            : $"Preview found {errors} issue(s). Fix missing tags or conflicts before renaming.",
            isWarning: errors > 0);
        RenameBlockReason = _lastPreview.CanRename ? "Ready to rename selected preview." : "Preview has issues.";
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

    private void ApplyBucket(BucketDefinitionViewModel bucket)
    {
        if (BucketSelectedItems.Count == 0)
        {
            SetStatus("Select one or more bucket images before applying a bucket.", isWarning: true);
            return;
        }

        var message = ApplyTagTo(BucketSelectedItems, bucket.Category, bucket.Value);
        SetStatus(message ?? $"Applied {bucket.Category}={bucket.Value} to {BucketSelectedItems.Count} image(s).", message is not null);
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
        _lastPreview = null;
        _lastPreviewScope = null;
        _ = SaveWorkInProgressAsync();
        RefreshBucketPage();
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

    private void RefreshBucketPage()
    {
        var workingSet = BucketWorkingSet.Filter(
            CurrentAllItems(),
            SelectedBucketPass.Pass,
            SelectedBucketFilter);
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
        OnPropertyChanged(nameof(BucketSelectionCountText));
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

    public void ShowWarning(string message)
    {
        SetStatus(message, isWarning: true);
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
        using var dialog = new WinForms.FolderBrowserDialog
        {
            Description = "Choose the root image folder to scan",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(FolderPath) ? FolderPath : string.Empty
        };

        if (dialog.ShowDialog() == WinForms.DialogResult.OK)
        {
            FolderPath = dialog.SelectedPath;
        }
    }

    private void CancelPreview()
    {
        _previewCancellation?.Cancel();
    }

    private bool FilterItem(object candidate)
    {
        if (candidate is not ImageItemViewModel item || string.IsNullOrWhiteSpace(FilterText))
        {
            return true;
        }

        return item.FileName.Contains(FilterText, StringComparison.OrdinalIgnoreCase) ||
            item.CurrentTagsText.Contains(FilterText, StringComparison.OrdinalIgnoreCase) ||
            item.FullPath.Contains(FilterText, StringComparison.OrdinalIgnoreCase);
    }

    private void RaiseCommandStates()
    {
        ScanCommand.RaiseCanExecuteChanged();
        PreviewRenameCommand.RaiseCanExecuteChanged();
        PreviewChangedCommand.RaiseCanExecuteChanged();
        CancelPreviewCommand.RaiseCanExecuteChanged();
        RenameSelectedCommand.RaiseCanExecuteChanged();
        ApplyDefaultToPageCommand.RaiseCanExecuteChanged();
        ClearSelectedTemporaryTagsCommand.RaiseCanExecuteChanged();
        ClearCurrentBucketPassTagsCommand.RaiseCanExecuteChanged();
        ClearAllTemporaryTagsCommand.RaiseCanExecuteChanged();
        NextBucketPageCommand.RaiseCanExecuteChanged();
        PreviousBucketPageCommand.RaiseCanExecuteChanged();
        InvertBucketSelectionCommand.RaiseCanExecuteChanged();
        SelectUntaggedBucketCommand.RaiseCanExecuteChanged();
    }

    private IReadOnlyList<ImageItem> CurrentSelectedItems()
    {
        return SelectedItems.Select(item => item.Item).ToList();
    }

    private IReadOnlyList<ImageItem> CurrentAllItems()
    {
        return Items.Select(item => item.Item).ToList();
    }

    private void UpdateRenameReadiness()
    {
        var readiness = RenameReadiness.Evaluate(CurrentSelectedItems());
        RenameBlockReason = readiness.CanPreview ? "Ready to preview selected images." : readiness.Message;
    }

    private void SetStatus(string message, bool isWarning = false)
    {
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
}

public sealed record BucketPassOptionViewModel(BucketPass Pass, string DisplayName)
{
    public override string ToString() => DisplayName;
}

public sealed record BucketDefinitionViewModel(string Category, string Value, string Label);

public enum BucketSelectionAction
{
    Invert,
    SelectUntagged
}
