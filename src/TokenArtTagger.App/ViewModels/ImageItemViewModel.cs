using System.Windows.Media;
using TokenArtTagger.Core;

namespace TokenArtTagger.App.ViewModels;

public sealed class ImageItemViewModel : ViewModelBase
{
    private ImageItem _item;
    private ImageSource? _thumbnail;
    private string? _proposedFileName;
    private string? _previewError;

    public ImageItemViewModel(ImageItem item)
    {
        _item = item;
    }

    public ImageItem Item => _item;

    public string FullPath => _item.FullPath;

    public string FileName => _item.FileName;

    public string ParsedTagsText => FormatTags(_item.ParsedTags);

    public string CurrentTagsText => FormatTags(_item.CurrentTags);

    public bool IsDirty => _item.IsDirty;

    public ImageSource? Thumbnail
    {
        get => _thumbnail;
        set => SetProperty(ref _thumbnail, value);
    }

    public string? ProposedFileName
    {
        get => _proposedFileName;
        set => SetProperty(ref _proposedFileName, value);
    }

    public string? PreviewError
    {
        get => _previewError;
        set => SetProperty(ref _previewError, value);
    }

    public void ApplyTag(string category, string value)
    {
        _item = _item with { CurrentTags = _item.CurrentTags.WithTag(category, value) };
        ProposedFileName = null;
        PreviewError = null;
        OnPropertyChanged(nameof(Item));
        OnPropertyChanged(nameof(CurrentTagsText));
        OnPropertyChanged(nameof(IsDirty));
    }

    public void ApplyPreview(RenamePreviewEntry? entry)
    {
        ProposedFileName = entry?.ProposedFileName;
        PreviewError = entry?.ErrorMessage;
    }

    private static string FormatTags(TagSet tags)
    {
        var style = tags.IsGeneric ? null : tags.WeaponOrStyle;
        return string.Join(" / ", new[] { tags.Gender, tags.Role, style, tags.Race }.Where(value => !string.IsNullOrWhiteSpace(value)));
    }
}
