using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace TokenArtTagger.Tests;

[TestClass]
public class MainWindowXamlBindingTests
{
    [TestMethod]
    [DataRow("FullPath")]
    [DataRow("FileName")]
    [DataRow("ParsedTagsText")]
    [DataRow("CurrentTagsText")]
    [DataRow("ProposedFileName")]
    [DataRow("PreviewError")]
    public void SelectableLibraryDetailTextBoxesUseOneWayBindings(string propertyName)
    {
        var xaml = ReadMainWindowXaml();

        StringAssert.Contains(
            xaml,
            $"Text=\"{{Binding {propertyName}, Mode=OneWay}}\"",
            $"Library details TextBox binding for {propertyName} must be OneWay. TextBox.Text defaults to TwoWay, which crashes when bound to read-only view-model properties.");
    }

    [TestMethod]
    public void ImageTileTemplateDoesNotStackInnerSelectedOverlay()
    {
        var xaml = ReadMainWindowXaml();

        StringAssert.Contains(xaml, "Template TargetType=\"{x:Type ListBoxItem}\"");
        StringAssert.Contains(xaml, "BorderBrush\" Value=\"#1D4ED8\"");
        StringAssert.Contains(xaml, "Needs tags");
        Assert.IsFalse(xaml.Contains("Grid.RowSpan=\"5\"", StringComparison.Ordinal), "Selected card outline should be drawn once by the card border, not by an inner full-card overlay.");
    }

    [TestMethod]
    public void BucketModeReusesSelectionDetailsTemplate()
    {
        var xaml = ReadMainWindowXaml();

        StringAssert.Contains(xaml, "x:Key=\"SelectedImageDetailsTemplate\"");
        StringAssert.Contains(xaml, "Content=\"{Binding SingleSelectionDetailItem}\" ContentTemplate=\"{StaticResource SelectedImageDetailsTemplate}\"");
        StringAssert.Contains(xaml, "Content=\"{Binding BucketSingleSelectionDetailItem}\" ContentTemplate=\"{StaticResource SelectedImageDetailsTemplate}\"");
        StringAssert.Contains(xaml, "Text=\"{Binding BucketSelectionAggregateText, Mode=OneWay}\"");
    }

    private static string ReadMainWindowXaml()
    {
        return File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "TokenArtTagger.App", "MainWindow.xaml"));
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TokenArtTagger.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate TokenArtTagger repository root.");
    }
}
