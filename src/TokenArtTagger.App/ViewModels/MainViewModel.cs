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
    private RenamePreview? _lastPreview;

    public MainViewModel()
    {
        ItemsView = CollectionViewSource.GetDefaultView(Items);
        ItemsView.Filter = FilterItem;
        TagButtons = new ObservableCollection<TagButtonViewModel>(
            TagSchema.TagButtons().Select(tag => new TagButtonViewModel(tag.Category, tag.Value, tag.Group)));

        BrowseCommand = new RelayCommand(_ => BrowseFolder());
        ScanCommand = new AsyncRelayCommand(_ => ScanAsync(), _ => !string.IsNullOrWhiteSpace(FolderPath));
        ApplyTagCommand = new RelayCommand(parameter => ApplyTag((TagButtonViewModel)parameter!), parameter => parameter is TagButtonViewModel);
        PreviewRenameCommand = new AsyncRelayCommand(_ => PreviewRenameAsync(), _ => SelectedItems.Count > 0);
        RenameSelectedCommand = new AsyncRelayCommand(_ => RenameSelectedAsync(), _ => _lastPreview?.CanRename == true);
        UndoLastBatchCommand = new RelayCommand(_ => StatusMessage = "Undo is not implemented in v0.1. Use the JSON undo log for manual recovery.");
    }

    public ObservableCollection<ImageItemViewModel> Items { get; } = [];

    public ObservableCollection<ImageItemViewModel> SelectedItems { get; } = [];

    public ObservableCollection<TagButtonViewModel> TagButtons { get; }

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

    public RelayCommand BrowseCommand { get; }

    public AsyncRelayCommand ScanCommand { get; }

    public RelayCommand ApplyTagCommand { get; }

    public AsyncRelayCommand PreviewRenameCommand { get; }

    public AsyncRelayCommand RenameSelectedCommand { get; }

    public RelayCommand UndoLastBatchCommand { get; }

    public async Task ScanAsync()
    {
        StatusMessage = "Scanning...";
        _lastPreview = null;
        Items.Clear();
        SelectedItems.Clear();
        _thumbnailService.Clear();

        var result = await FileScanner.ScanAsync(FolderPath);
        foreach (var item in result.Items.OrderBy(item => item.FileName, StringComparer.OrdinalIgnoreCase))
        {
            var viewModel = new ImageItemViewModel(item);
            Items.Add(viewModel);
            _ = LoadThumbnailAsync(viewModel);
        }

        StatusMessage = result.Errors.Count == 0
            ? $"Scanned {Items.Count} images."
            : $"Scanned {Items.Count} images with {result.Errors.Count} errors.";
        RaiseCommandStates();
    }

    public void ReplaceSelection(IEnumerable<ImageItemViewModel> selectedItems)
    {
        SelectedItems.Clear();
        foreach (var item in selectedItems)
        {
            SelectedItems.Add(item);
        }

        OnPropertyChanged(nameof(SelectedItem));
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
        StatusMessage = $"Applied {tag.Value} to {SelectedItems.Count} selected image(s).";
        RaiseCommandStates();
    }

    private async Task PreviewRenameAsync()
    {
        StatusMessage = "Building rename preview...";
        foreach (var item in Items)
        {
            item.ApplyPreview(null);
        }

        _lastPreview = await FileRenamer.BuildPreviewAsync(SelectedItems.Select(item => item.Item).ToList());
        foreach (var entry in _lastPreview.Entries)
        {
            SelectedItems.FirstOrDefault(item => item.FullPath == entry.Item.FullPath)?.ApplyPreview(entry);
        }

        var errors = _lastPreview.Entries.Count(entry => !entry.CanRename);
        StatusMessage = errors == 0
            ? $"Preview ready for {_lastPreview.Entries.Count} selected image(s)."
            : $"Preview found {errors} issue(s). Fix missing tags or conflicts before renaming.";
        RaiseCommandStates();
    }

    private async Task RenameSelectedAsync()
    {
        if (_lastPreview?.CanRename != true)
        {
            StatusMessage = "Preview the selected batch before renaming.";
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

    private async Task LoadThumbnailAsync(ImageItemViewModel item)
    {
        var thumbnail = await _thumbnailService.LoadThumbnailAsync(item.FullPath);
        System.Windows.Application.Current.Dispatcher.Invoke(() => item.Thumbnail = thumbnail);
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
        RenameSelectedCommand.RaiseCanExecuteChanged();
    }
}
