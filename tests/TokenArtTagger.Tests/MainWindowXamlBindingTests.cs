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
        StringAssert.Contains(xaml, "Needs");
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

    [TestMethod]
    public void ImageTileTemplateReservesFixedBottomStatusRow()
    {
        var xaml = ReadMainWindowXaml();

        StringAssert.Contains(xaml, "x:Name=\"TileStatusBadges\"");
        StringAssert.Contains(xaml, "<RowDefinition Height=\"32\" />");
        StringAssert.Contains(xaml, "TextTrimming=\"CharacterEllipsis\"");
        StringAssert.Contains(xaml, "LineStackingStrategy=\"BlockLineHeight\"");
        StringAssert.Contains(xaml, "ToolTip=\"{Binding FileName}\"");
        Assert.IsFalse(
            xaml.Contains("x:Name=\"TileStatusBadges\" Grid.Row=\"3\" VerticalAlignment=\"Bottom\"", StringComparison.Ordinal),
            "Status badges should be vertically centered in a row tall enough for badge padding instead of bottom-aligned into clipping.");
        Assert.IsFalse(
            xaml.Contains("StackPanel Grid.Row=\"4\" Orientation=\"Vertical\"", StringComparison.Ordinal),
            "Status badges should live in a fixed bottom row instead of a flexible StackPanel that can be clipped by wrapped tags.");
    }

    [TestMethod]
    public void LibraryAndBucketListsShareRectangleSelectionSafetyHandlers()
    {
        var xaml = ReadMainWindowXaml();

        Assert.AreEqual(2, CountOccurrences(xaml, "PreviewMouseLeftButtonDown=\"ImageList_PreviewMouseLeftButtonDown\""));
        Assert.AreEqual(2, CountOccurrences(xaml, "PreviewMouseLeftButtonUp=\"ImageList_PreviewMouseLeftButtonUp\""));
        Assert.AreEqual(2, CountOccurrences(xaml, "MouseMove=\"ImageList_MouseMove\""));
        Assert.AreEqual(2, CountOccurrences(xaml, "MouseLeave=\"ImageList_MouseLeave\""));
        Assert.AreEqual(2, CountOccurrences(xaml, "LostMouseCapture=\"ImageList_LostMouseCapture\""));
    }

    [TestMethod]
    public void ImageViewportSurfacesCanStartRectangleSelectionFromWhitespace()
    {
        var xaml = ReadMainWindowXaml();

        StringAssert.Contains(xaml, "x:Name=\"LibraryImageSurface\"");
        StringAssert.Contains(xaml, "x:Name=\"BucketImageSurface\"");
        Assert.AreEqual(2, CountOccurrences(xaml, "PreviewMouseLeftButtonDown=\"ImageSurface_PreviewMouseLeftButtonDown\""));
        StringAssert.Contains(xaml, "Background=\"Transparent\"");
    }

    private static string ReadMainWindowXaml()
    {
        return File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "TokenArtTagger.App", "MainWindow.xaml"));
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
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
