using System.Windows.Media;
using TokenArtTagger.Core;

namespace TokenArtTagger.App.ViewModels;

public sealed class ImageItemViewModel : ViewModelBase
{
    private ImageItem _item;
    private ImageSource? _thumbnail;
    private string? _proposedFileName;
    private string? _previewError;
    private bool _isCompleteForCurrentBucketPass;

    public ImageItemViewModel(ImageItem item)
    {
        _item = item;
    }

    public ImageItem Item => _item;

    public string FullPath => _item.FullPath;

    public string FileName => _item.FileName;

    public string DisplayName => ProposedFileName ?? CurrentTagsText;

    public string ParsedTagsText => FormatTags(_item.ParsedTags);

    public string CurrentTagsText => FormatTags(_item.CurrentTags);

    public bool IsDirty => _item.IsDirty;

    public TagIssueResult TagIssue => TokenArtTagger.Core.TagIssue.Evaluate(_item.CurrentTags);

    public bool HasIncompleteTags => TagIssue.Kind == TagIssueKind.Incomplete;

    public bool HasInvalidTags => TagIssue.Kind == TagIssueKind.Invalid;

    public bool HasRenameBlockingIssue => HasInvalidTags || !string.IsNullOrWhiteSpace(PreviewError);

    public bool IsCompleteForCurrentBucketPass
    {
        get => _isCompleteForCurrentBucketPass;
        set => SetProperty(ref _isCompleteForCurrentBucketPass, value);
    }

    public string TagIssueMessage => string.IsNullOrWhiteSpace(PreviewError)
        ? TagIssue.Message
        : PreviewError;

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
        set
        {
            if (SetProperty(ref _previewError, value))
            {
                OnPropertyChanged(nameof(HasRenameBlockingIssue));
                OnPropertyChanged(nameof(TagIssueMessage));
            }
        }
    }

    public void ApplyTag(string category, string value)
    {
        ReplaceItem(_item with { CurrentTags = _item.CurrentTags.WithTag(category, value) });
    }

    public void ReplaceItem(ImageItem item)
    {
        _item = item;
        ProposedFileName = null;
        PreviewError = null;
        OnPropertyChanged(nameof(Item));
        OnPropertyChanged(nameof(FileName));
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(ParsedTagsText));
        OnPropertyChanged(nameof(CurrentTagsText));
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(TagIssue));
        OnPropertyChanged(nameof(HasIncompleteTags));
        OnPropertyChanged(nameof(HasInvalidTags));
        OnPropertyChanged(nameof(HasRenameBlockingIssue));
        OnPropertyChanged(nameof(TagIssueMessage));
    }

    public void ApplyPreview(RenamePreviewEntry? entry)
    {
        ProposedFileName = entry?.ProposedFileName;
        PreviewError = entry?.ErrorMessage;
        OnPropertyChanged(nameof(DisplayName));
    }

    private static string FormatTags(TagSet tags)
    {
        var style = tags.IsGeneric ? null : tags.WeaponOrStyle;
        return string.Join(" / ", new[] { tags.Gender, tags.Role, style, tags.Race }.Where(value => !string.IsNullOrWhiteSpace(value)));
    }
}
