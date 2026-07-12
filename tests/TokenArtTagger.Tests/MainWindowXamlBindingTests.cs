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
        Assert.AreEqual(2, CountOccurrences(xaml, "PreviewMouseLeftButtonUp=\"ImageSurface_PreviewMouseLeftButtonUp\""));
        Assert.AreEqual(2, CountOccurrences(xaml, "MouseMove=\"ImageSurface_MouseMove\""));
        Assert.AreEqual(2, CountOccurrences(xaml, "MouseLeave=\"ImageSurface_MouseLeave\""));
        Assert.AreEqual(2, CountOccurrences(xaml, "LostMouseCapture=\"ImageSurface_LostMouseCapture\""));
        StringAssert.Contains(xaml, "Background=\"Transparent\"");
    }

    [TestMethod]
    public void ImageViewportLabelsFloatInsideSharedSelectionSurface()
    {
        var xaml = ReadMainWindowXaml();
        var codeBehind = ReadMainWindowCodeBehind();

        StringAssert.Contains(xaml, "IsHitTestVisible=\"False\"");
        StringAssert.Contains(xaml, "Panel.ZIndex=\"1\"");
        Assert.AreEqual(2, CountOccurrences(xaml, "Margin=\"10,38,10,10\""));
        Assert.AreEqual(2, CountOccurrences(xaml, "Margin=\"10,10,0,0\""));
        Assert.IsFalse(
            xaml.Contains("DockPanel.Dock=\"Top\" Text=\"Current Page\"", StringComparison.Ordinal),
            "Current Page should float inside the shared selection surface, not live in a separate DockPanel row.");
        StringAssert.Contains(codeBehind, "RubberBandForSurface(surfaceElement)");
        StringAssert.Contains(codeBehind, "TransformToAncestor(surface)");
        Assert.IsFalse(
            codeBehind.Contains("RectangleFromDrag(_rectangleStartPoint, endPoint, _rectangleList)", StringComparison.Ordinal),
            "The rubber-band rectangle must be measured against the full image surface, not the inner list.");
    }

    [TestMethod]
    public void SelectionSurfacesOwnOuterPanelPadding()
    {
        var xaml = ReadMainWindowXaml();

        Assert.IsFalse(
            xaml.Contains("x:Name=\"LibraryImageSurface\" Grid.Column=\"1\" Margin=\"12,0\" Background=\"#FFFFFF\" BorderBrush=\"#D7D3CA\" BorderThickness=\"1\" CornerRadius=\"6\" Padding=\"10\"", StringComparison.Ordinal),
            "Library selection surface should start at the rounded panel origin; padding belongs inside the selectable root surface.");
        Assert.IsFalse(
            xaml.Contains("x:Name=\"BucketImageSurface\" Grid.Column=\"1\" Margin=\"12,0\" Background=\"#FFFFFF\" BorderBrush=\"#D7D3CA\" BorderThickness=\"1\" CornerRadius=\"6\" Padding=\"10\"", StringComparison.Ordinal),
            "Bucket selection surface should start at the rounded panel origin; padding belongs inside the selectable root surface.");
        StringAssert.Contains(xaml, "<Border x:Name=\"LibraryImageSurface\" Grid.Column=\"1\" Margin=\"12,0\" Background=\"#FFFFFF\" BorderBrush=\"#D7D3CA\" BorderThickness=\"1\" CornerRadius=\"6\">");
        StringAssert.Contains(xaml, "<Border x:Name=\"BucketImageSurface\" Grid.Column=\"1\" Margin=\"12,0\" Background=\"#FFFFFF\" BorderBrush=\"#D7D3CA\" BorderThickness=\"1\" CornerRadius=\"6\">");
    }

    [TestMethod]
    public void CurrentPageRootGridsContainNonInteractiveRubberBandOverlay()
    {
        var xaml = ReadMainWindowXaml();
        var codeBehind = ReadMainWindowCodeBehind();

        StringAssert.Contains(xaml, "<Canvas x:Name=\"LibrarySelectionOverlay\"");
        StringAssert.Contains(xaml, "<Canvas x:Name=\"BucketSelectionOverlay\"");
        StringAssert.Contains(xaml, "x:Name=\"LibraryRubberBand\"");
        StringAssert.Contains(xaml, "x:Name=\"BucketRubberBand\"");
        StringAssert.Contains(xaml, "IsHitTestVisible=\"False\" Panel.ZIndex=\"1\"");
        StringAssert.Contains(xaml, "Visibility=\"Collapsed\" Fill=\"#2AB42318\" Stroke=\"#B42318\"");
        StringAssert.Contains(codeBehind, "Canvas.SetLeft(_rubberBandRectangle");
        StringAssert.Contains(codeBehind, "_rubberBandRectangle.Visibility = Visibility.Visible");
        Assert.IsFalse(
            codeBehind.Contains("RubberBandAdorner", StringComparison.Ordinal),
            "Rectangle selection should draw inside the shared root surface instead of using an adorner layer that can drift from the input surface.");
    }

    [TestMethod]
    public void CurrentPageRootGridsOwnRectangleSelectionInput()
    {
        var xaml = ReadMainWindowXaml();
        var codeBehind = ReadMainWindowCodeBehind();

        StringAssert.Contains(xaml, "<Grid x:Name=\"LibrarySelectionSurface\" Background=\"Transparent\" ClipToBounds=\"False\"");
        StringAssert.Contains(xaml, "<Grid x:Name=\"BucketSelectionSurface\" Background=\"Transparent\" ClipToBounds=\"False\"");
        Assert.AreEqual(2, CountOccurrences(xaml, "PreviewMouseLeftButtonDown=\"ImageSurface_PreviewMouseLeftButtonDown\""));
        Assert.AreEqual(2, CountOccurrences(xaml, "PreviewMouseLeftButtonUp=\"ImageSurface_PreviewMouseLeftButtonUp\""));
        Assert.AreEqual(2, CountOccurrences(xaml, "MouseMove=\"ImageSurface_MouseMove\""));
        Assert.AreEqual(2, CountOccurrences(xaml, "LostMouseCapture=\"ImageSurface_LostMouseCapture\""));
        StringAssert.Contains(codeBehind, "ReferenceEquals(surface, BucketSelectionSurface)");
        StringAssert.Contains(codeBehind, "return ReferenceEquals(list, BucketList) ? BucketSelectionSurface : LibrarySelectionSurface;");
        Assert.IsFalse(
            codeBehind.Contains("ReferenceEquals(sender, BucketImageSurface)", StringComparison.Ordinal),
            "Header-space mouse-down should be handled by the shared root grid, not the decorative outer border.");
    }

    [TestMethod]
    public void BucketShortcutHintsStayInBucketMode()
    {
        var xaml = ReadMainWindowXaml();

        StringAssert.Contains(xaml, "Text=\"{Binding BucketShortcutHintText}\"");
        StringAssert.Contains(xaml, "Text=\"{Binding ButtonLabel}\"");
        Assert.AreEqual(1, CountOccurrences(xaml, "BucketShortcutHintText"));
    }

    private static string ReadMainWindowXaml()
    {
        return File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "TokenArtTagger.App", "MainWindow.xaml"));
    }

    private static string ReadMainWindowCodeBehind()
    {
        return File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "TokenArtTagger.App", "MainWindow.xaml.cs"));
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
