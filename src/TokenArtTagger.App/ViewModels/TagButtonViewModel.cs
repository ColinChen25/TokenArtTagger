namespace TokenArtTagger.App.ViewModels;

public sealed record TagButtonViewModel(string Category, string Value, string Group)
{
    public string Label => Value;
}
