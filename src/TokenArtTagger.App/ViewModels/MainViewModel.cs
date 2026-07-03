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
    private string _folderPath = string.Empty;
    private string _filterText = string.Empty;
    private string _statusMessage = "Choose a folder to begin.";
    private string _renameBlockReason = "Select one or more images before previewing or renaming.";
    private RenamePreview? _lastPreview;
    private RenamePreviewScope? _lastPreviewScope;
    private CancellationTokenSource? _previewCancellation;

    public MainViewModel()
    {
        ItemsView = CollectionViewSource.GetDefaultView(Items);
        ItemsView.Filter = FilterItem;
        TagButtons = new ObservableCollection<TagButtonViewModel>(
            TagSchema.TagButtons().Select(tag => new TagButtonViewModel(tag.Category, tag.Value, tag.Group)));
        TagButtonsView = CollectionViewSource.GetDefaultView(TagButtons);
        TagButtonsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(TagButtonViewModel.Group)));

        BrowseCommand = new RelayCommand(_ => BrowseFolder());
        ScanCommand = new AsyncRelayCommand(_ => ScanAsync(), _ => !string.IsNullOrWhiteSpace(FolderPath));
        ApplyTagCommand = new RelayCommand(parameter => ApplyTag((TagButtonViewModel)parameter!), parameter => parameter is TagButtonViewModel);
        PreviewRenameCommand = new AsyncRelayCommand(_ => PreviewRenameAsync(RenamePreviewScope.Selected), _ => SelectedItems.Count > 0);
        PreviewChangedCommand = new AsyncRelayCommand(_ => PreviewRenameAsync(RenamePreviewScope.Changed), _ => Items.Any(item => item.IsDirty));
        CancelPreviewCommand = new RelayCommand(_ => CancelPreview(), _ => _previewCancellation is not null);
        RenameSelectedCommand = new AsyncRelayCommand(_ => RenameSelectedAsync(), _ => RenameReadiness.Evaluate(CurrentSelectedItems()).CanPreview);
        UndoLastBatchCommand = new RelayCommand(_ => StatusMessage = "Undo is not implemented in v0.1. Use the JSON undo log for manual recovery.");
    }

    public ObservableCollection<ImageItemViewModel> Items { get; } = [];

    public ObservableCollection<ImageItemViewModel> SelectedItems { get; } = [];

    public ObservableCollection<TagButtonViewModel> TagButtons { get; }

    public ICollectionView TagButtonsView { get; }

    public ICollectionView ItemsView { get; }

    public ImageItemViewModel? SelectedItem => SelectedItems.LastOrDefault();

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

    public string RenameBlockReason
    {
        get => _renameBlockReason;
        private set => SetProperty(ref _renameBlockReason, value);
    }

    public RelayCommand BrowseCommand { get; }

    public AsyncRelayCommand ScanCommand { get; }

    public RelayCommand ApplyTagCommand { get; }

    public AsyncRelayCommand PreviewRenameCommand { get; }

    public AsyncRelayCommand PreviewChangedCommand { get; }

    public RelayCommand CancelPreviewCommand { get; }

    public AsyncRelayCommand RenameSelectedCommand { get; }

    public RelayCommand UndoLastBatchCommand { get; }

    public async Task ScanAsync()
    {
        StatusMessage = "Scanning...";
        _lastPreview = null;
        _lastPreviewScope = null;
        Items.Clear();
        SelectedItems.Clear();
        _thumbnailService.ClearMemory();

        var result = await FileScanner.ScanAsync(FolderPath);
        foreach (var item in result.Items.OrderBy(item => item.FileName, StringComparer.OrdinalIgnoreCase))
        {
            var viewModel = new ImageItemViewModel(item);
            Items.Add(viewModel);
        }

        StatusMessage = result.Errors.Count == 0
            ? $"Scanned {Items.Count} images."
            : $"Scanned {Items.Count} images with {result.Errors.Count} errors.";
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
        UpdateRenameReadiness();
        RaiseCommandStates();
    }

    public void ApplyTag(TagButtonViewModel tag)
    {
        if (SelectedItems.Count == 0)
        {
            StatusMessage = "Select one or more images before applying a tag.";
            return;
        }

        foreach (var item in SelectedItems)
        {
            item.ApplyTag(tag.Category, tag.Value);
        }

        _lastPreview = null;
        _lastPreviewScope = null;
        StatusMessage = $"Applied {tag.Value} to {SelectedItems.Count} selected image(s).";
        UpdateRenameReadiness();
        RaiseCommandStates();
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
            StatusMessage = readiness.Message;
            RenameBlockReason = readiness.Message;
            RaiseCommandStates();
            return;
        }

        StatusMessage = $"Building rename preview for {targets.Count} {scope.ToString().ToLowerInvariant()} image(s)...";
        foreach (var item in Items)
        {
            item.ApplyPreview(null);
        }

        _previewCancellation?.Dispose();
        _previewCancellation = new CancellationTokenSource();
        CancelPreviewCommand.RaiseCanExecuteChanged();
        var progress = new Progress<RenamePreviewProgress>(step =>
        {
            StatusMessage = $"Hashing {step.Completed}/{step.Total}: {step.CurrentFileName}";
        });

        try
        {
            _lastPreview = await FileRenamer.BuildPreviewAsync(targets, progress, _previewCancellation.Token);
            _lastPreviewScope = scope;
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Rename preview canceled.";
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
        StatusMessage = errors == 0
            ? $"Preview ready for {_lastPreview.Entries.Count} selected image(s)."
            : $"Preview found {errors} issue(s). Fix missing tags or conflicts before renaming.";
        RaiseCommandStates();
    }

    private async Task RenameSelectedAsync()
    {
        if (_lastPreview?.CanRename != true || _lastPreviewScope != RenamePreviewScope.Selected)
        {
            await PreviewRenameAsync(RenamePreviewScope.Selected);
            if (_lastPreview?.CanRename == true)
            {
                StatusMessage = "Preview ready. Review the proposed filenames, then click Rename Selected again to confirm.";
            }

            return;
        }

        var confirmation = System.Windows.MessageBox.Show(
            $"Rename {_lastPreview.Entries.Count} selected file(s) in place? This cannot be undone automatically in v0.1.",
            "Confirm Rename",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirmation != MessageBoxResult.Yes)
        {
            StatusMessage = "Rename canceled.";
            return;
        }

        var result = await FileRenamer.RenameAsync(_lastPreview, FolderPath);
        StatusMessage = result.Errors.Count == 0
            ? $"Renamed {result.RenamedCount} file(s). Undo log: {result.UndoLogPath}"
            : $"Renamed {result.RenamedCount} file(s) with {result.Errors.Count} error(s). Undo log: {result.UndoLogPath}";

        await ScanAsync();
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
}

public enum RenamePreviewScope
{
    Selected,
    Changed
}
